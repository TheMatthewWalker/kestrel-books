import React, { useCallback, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Badge, Button, Empty, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, gbp, spacing, type } from '../theme';

type Line = { description: string; quantity: string; unitPrice: string; vatRate: number; accountId: string };

/**
 * Credit notes for one kind ('sales' | 'purchase', via route param):
 * list → create draft → post → allocate against open invoices or leave for refund.
 */
export default function CreditNotesScreen({ route, navigation }: any) {
  const { businessId, kind } = route.params; // 'sales' | 'purchase'
  const base = `/businesses/${businessId}/${kind}-credit-notes`;
  const [items, setItems] = useState<any[] | null>(null);
  const [mode, setMode] = useState<'list' | 'create' | 'allocate'>('list');
  const [contacts, setContacts] = useState<any[]>([]);
  const [accounts, setAccounts] = useState<any[]>([]);
  // create form
  const [contactId, setContactId] = useState('');
  const [number, setNumber] = useState('');
  const [date, setDate] = useState(new Date().toISOString().slice(0, 10));
  const [lines, setLines] = useState<Line[]>([]);
  // allocation
  const [allocating, setAllocating] = useState<any | null>(null);
  const [openInvoices, setOpenInvoices] = useState<any[]>([]);
  const [allocInvoiceId, setAllocInvoiceId] = useState('');
  const [allocAmount, setAllocAmount] = useState('');

  const load = useCallback(() => {
    navigation.setOptions({ title: kind === 'sales' ? 'Sales Credit Notes' : 'Purchase Credit Notes' });
    api.get(base).then(r => setItems(r.data)).catch(e => Alert.alert('Error', errorMessage(e)));
    api.get(`/businesses/${businessId}/${kind === 'sales' ? 'customers' : 'vendors'}`)
      .then(r => setContacts(r.data)).catch(() => {});
    api.get(`/businesses/${businessId}/accounts`)
      .then(r => setAccounts(r.data.filter((a: any) =>
        kind === 'sales' ? a.type === 'Income' : a.type === 'Expense' || a.type === 'Asset')))
      .catch(() => {});
  }, [businessId, kind]);
  useFocusEffect(load);

  const chip = (selected: boolean, label: string, onPress: () => void, key: string) => (
    <Text key={key} onPress={onPress}
      style={{
        paddingVertical: 6, paddingHorizontal: 12, borderRadius: 6, overflow: 'hidden',
        fontSize: 13, marginRight: 6, marginBottom: 6,
        backgroundColor: selected ? colors.ink : colors.badgeDraft,
        color: selected ? '#fff' : colors.ink, fontWeight: '600',
      }}>{label}</Text>
  );

  const startCreate = () => {
    setContactId(''); setNumber(''); setLines([{
      description: '', quantity: '1', unitPrice: '', vatRate: 0,
      accountId: accounts[0]?.id ?? '',
    }]);
    setMode('create');
  };

  const totals = lines.reduce((t, l) => {
    const net = (parseFloat(l.quantity) || 0) * (parseFloat(l.unitPrice) || 0);
    const vat = Math.round(net * (l.vatRate === 0 ? 0.2 : l.vatRate === 1 ? 0.05 : 0) * 100) / 100;
    return { net: t.net + net, vat: t.vat + vat };
  }, { net: 0, vat: 0 });

  const save = async () => {
    try {
      const r = await api.post(base, {
        contactId, number, date, dueDate: date, reference: '', notes: '',
        lines: lines.filter(l => l.unitPrice).map(l => ({
          itemId: null, description: l.description || 'Credit', quantity: parseFloat(l.quantity) || 0,
          unitPrice: parseFloat(l.unitPrice) || 0, vatRate: l.vatRate, accountId: l.accountId,
        })),
      });
      Alert.alert('Draft saved', 'Post it to hit the ledger, then allocate or refund.');
      setMode('list'); load();
      return r.data.id;
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const post = async (id: string) => {
    try {
      const r = await api.post(`${base}/${id}/post`);
      Alert.alert('Posted', `Journal #${r.data.journalNumber} — the mirror of an invoice.`);
      load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const startAllocate = async (cn: any) => {
    try {
      const r = await api.get(`/businesses/${businessId}/${kind}-invoices`);
      setOpenInvoices(r.data.filter((i: any) =>
        i.status === 'Posted' && i.grossTotal - i.amountPaid > 0.004 && i.contact === cn.contact));
      setAllocating(cn); setAllocInvoiceId(''); setAllocAmount('');
      setMode('allocate');
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const allocate = async () => {
    try {
      const r = await api.post(`${base}/${allocating.id}/allocate`, {
        invoiceId: allocInvoiceId, amount: parseFloat(allocAmount) || 0,
      });
      Alert.alert('Allocated',
        `Credit remaining ${gbp(r.data.creditNoteRemaining)} · invoice now outstanding ${gbp(r.data.invoiceOutstanding)}.`);
      setMode('list'); load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  if (items === null) return <Screen><Loading /></Screen>;

  if (mode === 'create') {
    return (
      <Screen>
        <Text style={type.title}>New {kind} credit note</Text>
        <Label>{kind === 'sales' ? 'Customer' : 'Supplier'}</Label>
        <View style={{ flexDirection: 'row', flexWrap: 'wrap' }}>
          {contacts.map(c => chip(contactId === c.id, c.name, () => setContactId(c.id), c.id))}
        </View>
        <Label>Credit note number</Label>
        <Input value={number} onChangeText={setNumber} placeholder="e.g. CN-001" />
        <Label>Date</Label>
        <Input value={date} onChangeText={setDate} placeholder="YYYY-MM-DD" />
        {lines.map((l, idx) => (
          <View key={idx} style={{ marginTop: spacing.m, borderTopWidth: 1, borderTopColor: colors.line, paddingTop: spacing.s }}>
            <Label>Line {idx + 1} — description</Label>
            <Input value={l.description}
              onChangeText={v => setLines(ls => ls.map((x, i) => i === idx ? { ...x, description: v } : x))}
              placeholder="What is being credited?" />
            <View style={{ flexDirection: 'row', gap: spacing.s }}>
              <View style={{ flex: 1 }}>
                <Label>Qty</Label>
                <Input value={l.quantity} keyboardType="decimal-pad"
                  onChangeText={v => setLines(ls => ls.map((x, i) => i === idx ? { ...x, quantity: v } : x))} />
              </View>
              <View style={{ flex: 2 }}>
                <Label>Unit price (net)</Label>
                <Input value={l.unitPrice} keyboardType="decimal-pad"
                  onChangeText={v => setLines(ls => ls.map((x, i) => i === idx ? { ...x, unitPrice: v } : x))} />
              </View>
            </View>
            <Label>VAT</Label>
            <View style={{ flexDirection: 'row' }}>
              {[[0, '20%'], [1, '5%'], [2, 'Zero'], [3, 'Exempt']].map(([v, lab]) =>
                chip(l.vatRate === v, lab as string,
                  () => setLines(ls => ls.map((x, i) => i === idx ? { ...x, vatRate: v as number } : x)), `v${v}`))}
            </View>
            <Label>Account</Label>
            <View style={{ flexDirection: 'row', flexWrap: 'wrap' }}>
              {accounts.slice(0, 10).map(a =>
                chip(l.accountId === a.id, `${a.code} ${a.name}`,
                  () => setLines(ls => ls.map((x, i) => i === idx ? { ...x, accountId: a.id } : x)), a.id))}
            </View>
          </View>
        ))}
        <Button kind="ghost" title="+ Add line"
          onPress={() => setLines(ls => [...ls, { description: '', quantity: '1', unitPrice: '', vatRate: 0, accountId: accounts[0]?.id ?? '' }])} />
        <LedgerRow left="Net" amount={gbp(totals.net)} />
        <LedgerRow left="VAT" amount={gbp(totals.vat)} />
        <LedgerRow left="Gross credit" amount={gbp(totals.net + totals.vat)} />
        <Button title="Save draft" onPress={save} disabled={!contactId || !number} />
        <Button kind="ghost" title="Cancel" onPress={() => setMode('list')} />
      </Screen>
    );
  }

  if (mode === 'allocate') {
    return (
      <Screen>
        <Text style={type.title}>Allocate {allocating.number}</Text>
        <Text style={[type.body, { marginTop: spacing.xs }]}>
          Unapplied credit: {gbp(allocating.grossTotal - allocating.amountPaid)}. Allocation is a contra
          within {kind === 'sales' ? 'trade debtors' : 'trade creditors'} — no journal, both documents
          simply owe less.
        </Text>
        <Label>Open invoices for {allocating.contact}</Label>
        {openInvoices.length === 0 ? (
          <Empty text="No open invoices for this contact — record a refund from the Money screen instead." />
        ) : openInvoices.map(i => (
          <LedgerRow key={i.id} left={`${i.number}`} sub={`outstanding ${gbp(i.grossTotal - i.amountPaid)}`}
            amount={allocInvoiceId === i.id ? '✓ selected' : 'select'}
            onPress={() => {
              setAllocInvoiceId(i.id);
              setAllocAmount(String(Math.min(
                allocating.grossTotal - allocating.amountPaid,
                i.grossTotal - i.amountPaid).toFixed(2)));
            }} />
        ))}
        <Label>Amount to allocate</Label>
        <Input value={allocAmount} onChangeText={setAllocAmount} keyboardType="decimal-pad" />
        <Button title="Allocate" onPress={allocate} disabled={!allocInvoiceId} />
        <Button kind="ghost" title="Back" onPress={() => setMode('list')} />
      </Screen>
    );
  }

  return (
    <Screen>
      {items.length === 0 ? <Empty text={`No ${kind} credit notes yet.`} /> : items.map(c => (
        <View key={c.id}>
          <View style={{ flexDirection: 'row', alignItems: 'center' }}>
            <View style={{ flex: 1 }}>
              <LedgerRow left={`${c.number} — ${c.contact}`}
                sub={`${c.date}  ·  applied ${gbp(c.amountPaid)} of ${gbp(c.grossTotal)}`}
                amount={gbp(c.grossTotal)}
                amountColor={kind === 'sales' ? colors.debit : colors.credit} />
            </View>
            <View style={{ marginLeft: 6 }}><Badge status={c.status} /></View>
          </View>
          <View style={{ flexDirection: 'row', gap: spacing.s, marginBottom: spacing.s }}>
            {c.status === 'Draft' && (
              <Button kind="ghost" title="Post" onPress={() => post(c.id)} />
            )}
            {c.status === 'Posted' && c.grossTotal - c.amountPaid > 0.004 && (
              <Button kind="ghost" title="Allocate to invoice" onPress={() => startAllocate(c)} />
            )}
          </View>
        </View>
      ))}
      <Button title={`New ${kind} credit note`} onPress={startCreate} />
    </Screen>
  );
}
