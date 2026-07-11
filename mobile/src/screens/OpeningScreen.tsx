import React, { useCallback, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import * as DocumentPicker from 'expo-document-picker';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Button, Empty, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, gbp, spacing, type } from '../theme';

export default function OpeningScreen({ route }: any) {
  const { businessId } = route.params;
  const [status, setStatus] = useState<any | null>(null);
  const [parsed, setParsed] = useState<any | null>(null);
  const [conversionDate, setConversionDate] = useState(new Date().toISOString().slice(0, 10));
  const [contacts, setContacts] = useState<{ customers: any[]; vendors: any[] }>({ customers: [], vendors: [] });
  const [invKind, setInvKind] = useState<'sales' | 'purchase'>('sales');
  const [contactId, setContactId] = useState('');
  const [number, setNumber] = useState('');
  const [gross, setGross] = useState('');
  const [invDate, setInvDate] = useState('');
  const [dueDate, setDueDate] = useState('');

  const load = useCallback(() => {
    api.get(`/businesses/${businessId}/opening/status`)
      .then(r => setStatus(r.data)).catch(e => Alert.alert('Error', errorMessage(e)));
    Promise.all([
      api.get(`/businesses/${businessId}/customers`),
      api.get(`/businesses/${businessId}/vendors`),
    ]).then(([c, v]) => setContacts({ customers: c.data, vendors: v.data })).catch(() => {});
  }, [businessId]);
  useFocusEffect(load);

  const pickCsv = async () => {
    const res = await DocumentPicker.getDocumentAsync({ type: ['text/csv', 'text/plain'], copyToCacheDirectory: true });
    if (res.canceled || !res.assets?.length) return;
    try {
      const form = new FormData();
      form.append('file', { uri: res.assets[0].uri, name: res.assets[0].name, type: 'text/csv' } as any);
      const r = await api.post(`/businesses/${businessId}/opening/trial-balance/parse-csv`, form,
        { headers: { 'Content-Type': 'multipart/form-data' } });
      setParsed(r.data);
    } catch (e) { Alert.alert('Parse failed', errorMessage(e)); }
  };

  const commitTb = async () => {
    try {
      const r = await api.post(`/businesses/${businessId}/opening/trial-balance`, {
        conversionDate,
        lines: parsed.matched.map((m: any) => ({ accountId: m.accountId, debit: m.debit, credit: m.credit })),
      });
      Alert.alert('Opening balances posted', `Journal #${r.data.journalNumber} — dated the day before conversion.`);
      setParsed(null);
      load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const addInvoice = async () => {
    try {
      await api.post(`/businesses/${businessId}/opening/${invKind}-invoices`, {
        contactId, number, gross: parseFloat(gross) || 0,
        date: invDate || conversionDate, dueDate: dueDate || conversionDate,
      });
      Alert.alert('Added', 'Open invoice recorded — no journal, its value sits in the TB control account.');
      setNumber(''); setGross('');
      load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const chip = (selected: boolean, label: string, onPress: () => void, key: string) => (
    <Text key={key} onPress={onPress}
      style={{
        paddingVertical: 6, paddingHorizontal: 12, borderRadius: 6, overflow: 'hidden', fontSize: 13,
        backgroundColor: selected ? colors.ink : colors.badgeDraft,
        color: selected ? '#fff' : colors.ink, fontWeight: '600',
      }}>{label}</Text>
  );

  if (status === null) return <Screen><Loading /></Screen>;

  const contactList = invKind === 'sales' ? contacts.customers : contacts.vendors;
  const debtorsAgree = Math.abs((status.tbDebtorsControl ?? 0) - (status.enteredOpenDebtors ?? 0)) < 0.005;
  const creditorsAgree = Math.abs((status.tbCreditorsControl ?? 0) - (status.enteredOpenCreditors ?? 0)) < 0.005;

  return (
    <Screen>
      {!status.hasOpeningJournal ? (
        <>
          <Text style={type.title}>1 — Opening trial balance</Text>
          <Text style={[type.body, { marginTop: spacing.xs }]}>
            Export the closing TB from the previous system as CSV (code, debit, credit)
            and import it here. It posts as one journal dated the day before conversion.
          </Text>
          <Label>Conversion date (first day on KestrelBooks)</Label>
          <Input value={conversionDate} onChangeText={setConversionDate} placeholder="YYYY-MM-DD" />
          <Button title="Import TB from CSV" onPress={pickCsv} />
          {parsed && (
            <>
              <Label>Review</Label>
              <Text style={type.body}>
                {parsed.matched.length} rows matched · debits {gbp(parsed.totalDebits)} · credits {gbp(parsed.totalCredits)}
              </Text>
              {parsed.totalDebits !== parsed.totalCredits && (
                <Text style={{ color: colors.debit, fontSize: 13, marginTop: spacing.xs }}>
                  Does not balance — fix the CSV before committing.
                </Text>
              )}
              {parsed.unmatched.length > 0 && (
                <>
                  <Text style={{ color: colors.debit, fontSize: 13, marginTop: spacing.xs }}>
                    Unmatched codes (add these accounts first, or fix the codes):
                  </Text>
                  {parsed.unmatched.map((u: any) => (
                    <LedgerRow key={u.code} left={u.code} amount={gbp(u.debit || u.credit)} />
                  ))}
                </>
              )}
              {parsed.matched.slice(0, 8).map((m: any) => (
                <LedgerRow key={m.accountId} left={`${m.code}  ${m.name}`}
                  amount={m.debit > 0 ? `Dr ${gbp(m.debit)}` : `Cr ${gbp(m.credit)}`} />
              ))}
              {parsed.matched.length > 8 && <Empty text={`…and ${parsed.matched.length - 8} more rows`} />}
              <Button title="Post opening trial balance"
                onPress={commitTb}
                disabled={parsed.unmatched.length > 0 || parsed.totalDebits !== parsed.totalCredits} />
            </>
          )}
        </>
      ) : (
        <>
          <Text style={[type.body, { color: colors.credit }]}>
            ✓ Opening TB posted (journal #{status.openingJournalNumber}, {status.conversionDate})
          </Text>

          <Text style={[type.title, { marginTop: spacing.l }]}>2 — Open invoices</Text>
          <Text style={[type.body, { marginTop: spacing.xs }]}>
            Enter each unpaid invoice from the old system so receipts can settle them.
            These carry no journal — their total should agree with the TB control account.
          </Text>
          <View style={{ marginTop: spacing.s }}>
            <LedgerRow left="TB Trade Debtors control" amount={gbp(status.tbDebtorsControl)} />
            <LedgerRow left="Open sales invoices entered" amount={gbp(status.enteredOpenDebtors)}
              amountColor={debtorsAgree ? colors.credit : colors.debit} />
            <LedgerRow left="TB Trade Creditors control" amount={gbp(status.tbCreditorsControl)} />
            <LedgerRow left="Open purchase invoices entered" amount={gbp(status.enteredOpenCreditors)}
              amountColor={creditorsAgree ? colors.credit : colors.debit} />
          </View>

          <Label>Add an open invoice</Label>
          <View style={{ flexDirection: 'row', gap: spacing.s }}>
            {chip(invKind === 'sales', 'Customer owes us', () => { setInvKind('sales'); setContactId(''); }, 's')}
            {chip(invKind === 'purchase', 'We owe supplier', () => { setInvKind('purchase'); setContactId(''); }, 'p')}
          </View>
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs, marginTop: spacing.s }}>
            {contactList.map((c: any) => chip(contactId === c.id, c.name, () => setContactId(c.id), c.id))}
          </View>
          <Label>Invoice number (from the old system)</Label>
          <Input value={number} onChangeText={setNumber} placeholder="e.g. INV-1042" />
          <Label>Amount outstanding (gross)</Label>
          <Input value={gross} onChangeText={setGross} keyboardType="decimal-pad" placeholder="0.00" />
          <Label>Invoice date</Label>
          <Input value={invDate} onChangeText={setInvDate} placeholder="YYYY-MM-DD" />
          <Label>Due date</Label>
          <Input value={dueDate} onChangeText={setDueDate} placeholder="YYYY-MM-DD" />
          <Button title="Add open invoice" onPress={addInvoice} />

          <Text style={{ color: colors.muted, fontSize: 12, marginTop: spacing.m }}>
            Opening stock quantities are set per item from the Inventory screen roadmap;
            the API endpoint (POST /opening/stock) is live for bulk conversion.
          </Text>
        </>
      )}
    </Screen>
  );
}
