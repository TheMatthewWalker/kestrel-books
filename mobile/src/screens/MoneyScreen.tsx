import React, { useCallback, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Button, Empty, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, gbp, spacing } from '../theme';

export default function MoneyScreen({ route }: any) {
  const { businessId } = route.params;
  const [txs, setTxs] = useState<any[] | null>(null);
  const [banks, setBanks] = useState<any[]>([]);
  const [accounts, setAccounts] = useState<any[]>([]);
  const [openSales, setOpenSales] = useState<any[]>([]);
  const [openPurch, setOpenPurch] = useState<any[]>([]);
  const [adding, setAdding] = useState(false);

  const [direction, setDirection] = useState(0); // 0 in, 1 out
  const [amount, setAmount] = useState('');
  const [reference, setReference] = useState('');
  const [date, setDate] = useState(new Date().toISOString().slice(0, 10));
  const [bankId, setBankId] = useState('');
  const [settleInvoiceId, setSettleInvoiceId] = useState<string | null>(null);
  const [directAccountId, setDirectAccountId] = useState<string | null>(null);

  const load = useCallback(() => {
    api.get(`/businesses/${businessId}/money`).then(r => setTxs(r.data)).catch(() => setTxs([]));
    api.get(`/businesses/${businessId}/accounts`).then(r => {
      setBanks(r.data.filter((a: any) => a.isBank));
      setAccounts(r.data.filter((a: any) => !a.isBank && !a.systemTag));
      if (!bankId) {
        const d = r.data.find((a: any) => a.systemTag === 'BANK_DEFAULT') ?? r.data.find((a: any) => a.isBank);
        if (d) setBankId(d.id);
      }
    });
    api.get(`/businesses/${businessId}/sales-invoices`)
      .then(r => setOpenSales(r.data.filter((i: any) => i.status === 1 && i.amountPaid < i.grossTotal)));
    api.get(`/businesses/${businessId}/purchase-invoices`)
      .then(r => setOpenPurch(r.data.filter((i: any) => i.status === 1 && i.amountPaid < i.grossTotal)));
  }, [businessId]);
  useFocusEffect(load);

  const create = async () => {
    try {
      const res = await api.post(`/businesses/${businessId}/money`, {
        direction, date, reference, amount: parseFloat(amount) || 0,
        bankAccountId: bankId,
        customerId: null, vendorId: null,
        salesInvoiceId: direction === 0 ? settleInvoiceId : null,
        purchaseInvoiceId: direction === 1 ? settleInvoiceId : null,
        directAccountId: settleInvoiceId ? null : directAccountId,
        notes: null,
      });
      const r = await api.post(`/businesses/${businessId}/money/${res.data.id}/post`);
      Alert.alert('Posted', `Journal #${r.data.journalNumber} created.`);
      setAdding(false); setAmount(''); setReference(''); setSettleInvoiceId(null); setDirectAccountId(null);
      load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const chip = (selected: boolean, label: string, onPress: () => void, key?: string) => (
    <Text key={key ?? label} onPress={onPress}
      style={{
        paddingVertical: 6, paddingHorizontal: 12, borderRadius: 6, overflow: 'hidden', fontSize: 13,
        backgroundColor: selected ? colors.ink : colors.badgeDraft,
        color: selected ? '#fff' : colors.ink, fontWeight: '600',
      }}>{label}</Text>
  );

  const openInvoices = direction === 0 ? openSales : openPurch;

  return (
    <Screen>
      {!adding && <Button title="Record money in / out" onPress={() => setAdding(true)} />}
      {adding && (
        <>
          <Label>Direction</Label>
          <View style={{ flexDirection: 'row', gap: spacing.s }}>
            {chip(direction === 0, 'Money in (receipt)', () => { setDirection(0); setSettleInvoiceId(null); })}
            {chip(direction === 1, 'Money out (payment)', () => { setDirection(1); setSettleInvoiceId(null); })}
          </View>
          <Label>Bank account</Label>
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs }}>
            {banks.map(b => chip(bankId === b.id, `${b.code} ${b.name}`, () => setBankId(b.id), b.id))}
          </View>
          <Label>Amount</Label>
          <Input value={amount} onChangeText={setAmount} keyboardType="decimal-pad" placeholder="0.00" />
          <Label>Reference</Label>
          <Input value={reference} onChangeText={setReference} placeholder="e.g. BACS ref" />
          <Label>Date</Label>
          <Input value={date} onChangeText={setDate} placeholder="YYYY-MM-DD" />

          {openInvoices.length > 0 && (
            <>
              <Label>Settle an open invoice (optional)</Label>
              <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs }}>
                {openInvoices.map((i: any) =>
                  chip(settleInvoiceId === i.id,
                    `${i.number} · ${gbp(i.grossTotal - i.amountPaid)}`,
                    () => setSettleInvoiceId(settleInvoiceId === i.id ? null : i.id), i.id))}
              </View>
            </>
          )}
          {!settleInvoiceId && (
            <>
              <Label>Or post directly to account</Label>
              <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs }}>
                {accounts.slice(0, 12).map((a: any) =>
                  chip(directAccountId === a.id, `${a.code} ${a.name.slice(0, 16)}`,
                    () => setDirectAccountId(a.id), a.id))}
              </View>
            </>
          )}
          <Button title="Post to ledger" onPress={create} />
          <Button kind="ghost" title="Cancel" onPress={() => setAdding(false)} />
        </>
      )}

      <Label>Recent transactions</Label>
      {txs === null ? <Loading /> : txs.length === 0 ? <Empty text="Nothing recorded yet." /> :
        txs.map(t => (
          <LedgerRow key={t.id} left={t.reference || (t.direction === 0 ? 'Receipt' : 'Payment')}
            sub={t.date} amount={gbp(t.amount)}
            amountColor={t.direction === 0 ? colors.credit : colors.debit} />
        ))}
    </Screen>
  );
}
