import React, { useCallback, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Badge, Button, Empty, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, gbp, spacing, type } from '../theme';

type Line = { description: string; quantity: string; unitPrice: string; vatRate: number; accountId: string };

const vatOptions = [
  { label: '20%', value: 0 }, { label: '5%', value: 1 },
  { label: '0%', value: 2 }, { label: 'Exempt', value: 3 },
];

export default function InvoiceFormScreen({ route, navigation }: any) {
  const { businessId, kind, invoiceId } = route.params;
  const isSales = kind === 'sales';
  const [contacts, setContacts] = useState<any[]>([]);
  const [accounts, setAccounts] = useState<any[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [status, setStatus] = useState(0);
  const [contactId, setContactId] = useState('');
  const [number, setNumber] = useState('');
  const [date, setDate] = useState(new Date().toISOString().slice(0, 10));
  const [dueDate, setDueDate] = useState(new Date(Date.now() + 30 * 86400000).toISOString().slice(0, 10));
  const [lines, setLines] = useState<Line[]>([]);

  useFocusEffect(useCallback(() => {
    (async () => {
      try {
        const [cRes, aRes] = await Promise.all([
          api.get(`/businesses/${businessId}/${isSales ? 'customers' : 'vendors'}`),
          api.get(`/businesses/${businessId}/accounts`),
        ]);
        setContacts(cRes.data);
        const rel = aRes.data.filter((a: any) => isSales ? a.type === 3 : a.type === 4 || a.type === 0);
        setAccounts(rel);
        if (invoiceId) {
          const inv = (await api.get(`/businesses/${businessId}/${kind}-invoices/${invoiceId}`)).data;
          setStatus(inv.status);
          setContactId(inv.customerId ?? inv.vendorId);
          setNumber(inv.number);
          setDate(inv.date); setDueDate(inv.dueDate);
          setLines(inv.lines.map((l: any) => ({
            description: l.description, quantity: String(l.quantity),
            unitPrice: String(l.unitPrice), vatRate: l.vatRate, accountId: l.accountId,
          })));
        } else if (rel.length > 0) {
          setLines([{ description: '', quantity: '1', unitPrice: '', vatRate: 0, accountId: rel[0].id }]);
        }
        setLoaded(true);
      } catch (e) { Alert.alert('Error', errorMessage(e)); }
    })();
  }, [businessId, invoiceId]));

  const totals = lines.reduce((t, l) => {
    const net = (parseFloat(l.quantity) || 0) * (parseFloat(l.unitPrice) || 0);
    const vat = net * (l.vatRate === 0 ? 0.2 : l.vatRate === 1 ? 0.05 : 0);
    return { net: t.net + net, vat: t.vat + vat };
  }, { net: 0, vat: 0 });

  const payload = () => ({
    contactId, number, date, dueDate, reference: null, notes: null,
    lines: lines.map(l => ({
      itemId: null, description: l.description, quantity: parseFloat(l.quantity) || 0,
      unitPrice: parseFloat(l.unitPrice) || 0, vatRate: l.vatRate, accountId: l.accountId,
    })),
  });

  const save = async (thenPost: boolean) => {
    try {
      let id = invoiceId;
      if (id) await api.put(`/businesses/${businessId}/${kind}-invoices/${id}`, payload());
      else id = (await api.post(`/businesses/${businessId}/${kind}-invoices`, payload())).data.id;
      if (thenPost) {
        const r = await api.post(`/businesses/${businessId}/${kind}-invoices/${id}/post`);
        Alert.alert('Posted', `Journal #${r.data.journalNumber} created.`);
      }
      navigation.goBack();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  if (!loaded) return <Screen><Loading /></Screen>;
  const locked = status !== 0;

  return (
    <Screen>
      {locked && <Badge status={status} />}
      <Label>{isSales ? 'Customer' : 'Vendor'}</Label>
      {contacts.length === 0 ? <Empty text={`Add a ${isSales ? 'customer' : 'vendor'} first.`} /> : (
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs }}>
          {contacts.map(c => (
            <Text key={c.id} onPress={() => !locked && setContactId(c.id)}
              style={{
                paddingVertical: 6, paddingHorizontal: 12, borderRadius: 6, overflow: 'hidden', fontSize: 13,
                backgroundColor: contactId === c.id ? colors.ink : colors.badgeDraft,
                color: contactId === c.id ? '#fff' : colors.ink, fontWeight: '600',
              }}>{c.name}</Text>
          ))}
        </View>
      )}
      <Label>Invoice number</Label>
      <Input value={number} onChangeText={setNumber} editable={!locked} placeholder={isSales ? 'INV-0001' : "Supplier's invoice no."} />
      <Label>Date</Label>
      <Input value={date} onChangeText={setDate} editable={!locked} placeholder="YYYY-MM-DD" />
      <Label>Due date</Label>
      <Input value={dueDate} onChangeText={setDueDate} editable={!locked} placeholder="YYYY-MM-DD" />

      <Label>Lines</Label>
      {lines.map((l, idx) => (
        <View key={idx} style={{
          backgroundColor: '#fff', borderWidth: 1, borderColor: colors.rule,
          borderRadius: 8, padding: spacing.s, marginBottom: spacing.s,
        }}>
          <Input value={l.description} editable={!locked} placeholder="Description"
            onChangeText={v => setLines(ls => ls.map((x, i) => i === idx ? { ...x, description: v } : x))} />
          <View style={{ flexDirection: 'row', gap: spacing.s, marginTop: spacing.s }}>
            <Input style={{ flex: 1 }} value={l.quantity} editable={!locked} keyboardType="decimal-pad" placeholder="Qty"
              onChangeText={v => setLines(ls => ls.map((x, i) => i === idx ? { ...x, quantity: v } : x))} />
            <Input style={{ flex: 2 }} value={l.unitPrice} editable={!locked} keyboardType="decimal-pad" placeholder="Unit price (net)"
              onChangeText={v => setLines(ls => ls.map((x, i) => i === idx ? { ...x, unitPrice: v } : x))} />
          </View>
          <View style={{ flexDirection: 'row', gap: spacing.xs, marginTop: spacing.s, flexWrap: 'wrap' }}>
            {vatOptions.map(v => (
              <Text key={v.value} onPress={() => !locked && setLines(ls => ls.map((x, i) => i === idx ? { ...x, vatRate: v.value } : x))}
                style={{
                  paddingVertical: 4, paddingHorizontal: 10, borderRadius: 6, overflow: 'hidden', fontSize: 12,
                  backgroundColor: l.vatRate === v.value ? colors.inkSoft : colors.badgeDraft,
                  color: l.vatRate === v.value ? '#fff' : colors.ink, fontWeight: '600',
                }}>VAT {v.label}</Text>
            ))}
          </View>
          <View style={{ flexDirection: 'row', gap: spacing.xs, marginTop: spacing.s, flexWrap: 'wrap' }}>
            {accounts.slice(0, 8).map(a => (
              <Text key={a.id} onPress={() => !locked && setLines(ls => ls.map((x, i) => i === idx ? { ...x, accountId: a.id } : x))}
                style={{
                  paddingVertical: 4, paddingHorizontal: 10, borderRadius: 6, overflow: 'hidden', fontSize: 12,
                  backgroundColor: l.accountId === a.id ? colors.inkSoft : colors.badgeDraft,
                  color: l.accountId === a.id ? '#fff' : colors.ink,
                }}>{a.code} {a.name.length > 18 ? a.name.slice(0, 18) + '…' : a.name}</Text>
            ))}
          </View>
        </View>
      ))}
      {!locked && (
        <Button kind="ghost" title="Add line"
          onPress={() => setLines(ls => [...ls, { description: '', quantity: '1', unitPrice: '', vatRate: 0, accountId: accounts[0]?.id ?? '' }])} />
      )}

      <View style={{ marginTop: spacing.m }}>
        <LedgerRow left="Net" amount={gbp(totals.net)} />
        <LedgerRow left="VAT" amount={gbp(totals.vat)} />
        <LedgerRow left="Gross" amount={gbp(totals.net + totals.vat)} />
      </View>

      {!locked && (
        <>
          <Button kind="ghost" title="Save draft" onPress={() => save(false)} />
          <Button title="Save & post to ledger" onPress={() => save(true)} />
          <Text style={[type.body, { marginTop: spacing.s, fontSize: 12, color: colors.muted }]}>
            Posting creates the double entry automatically
            ({isSales ? 'Dr Trade Debtors, Cr Sales, Cr Output VAT' : 'Dr Expense, Dr Input VAT, Cr Trade Creditors'})
            and locks the invoice. Corrections after posting are made by reversal, keeping the audit trail.
          </Text>
        </>
      )}
    </Screen>
  );
}
