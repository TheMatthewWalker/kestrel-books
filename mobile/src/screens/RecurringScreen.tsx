import React, { useCallback, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Badge, Button, Empty, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, gbp, spacing, type } from '../theme';

const FREQ = ['Weekly', 'Monthly', 'Quarterly', 'Yearly'];

export default function RecurringScreen({ route }: any) {
  const { businessId } = route.params;
  const [items, setItems] = useState<any[] | null>(null);
  const [mode, setMode] = useState<'list' | 'create'>('list');
  const [customers, setCustomers] = useState<any[]>([]);
  const [accounts, setAccounts] = useState<any[]>([]);
  // form
  const [customerId, setCustomerId] = useState('');
  const [name, setName] = useState('');
  const [prefix, setPrefix] = useState('REC');
  const [freq, setFreq] = useState(1);
  const [terms, setTerms] = useState('30');
  const [nextRun, setNextRun] = useState(new Date().toISOString().slice(0, 10));
  const [autoPost, setAutoPost] = useState(false);
  const [desc, setDesc] = useState('');
  const [price, setPrice] = useState('');
  const [vatRate, setVatRate] = useState(0);
  const [accountId, setAccountId] = useState('');

  const load = useCallback(() => {
    api.get(`/businesses/${businessId}/recurring-invoices`)
      .then(r => setItems(r.data)).catch(e => Alert.alert('Error', errorMessage(e)));
    api.get(`/businesses/${businessId}/customers`).then(r => setCustomers(r.data)).catch(() => {});
    api.get(`/businesses/${businessId}/accounts`)
      .then(r => { const inc = r.data.filter((a: any) => a.type === 3); setAccounts(inc);
        if (inc[0]) setAccountId(inc[0].id); }).catch(() => {});
  }, [businessId]);
  useFocusEffect(load);

  const chip = (sel: boolean, label: string, onPress: () => void, key: string) => (
    <Text key={key} onPress={onPress} style={{
      paddingVertical: 6, paddingHorizontal: 12, borderRadius: 6, overflow: 'hidden', fontSize: 13,
      marginRight: 6, marginBottom: 6,
      backgroundColor: sel ? colors.ink : colors.badgeDraft, color: sel ? '#fff' : colors.ink, fontWeight: '600',
    }}>{label}</Text>
  );

  const create = async () => {
    try {
      await api.post(`/businesses/${businessId}/recurring-invoices`, {
        customerId, name, numberPrefix: prefix, frequency: freq,
        paymentTermsDays: parseInt(terms) || 30, nextRunDate: nextRun, endDate: null, autoPost,
        lines: [{ itemId: null, description: desc || name, quantity: 1,
                  unitPrice: parseFloat(price) || 0, vatRate, accountId }],
      });
      Alert.alert('Template created', autoPost
        ? 'Invoices will be generated and posted automatically on schedule.'
        : 'Invoices will be generated as drafts for you to review and post.');
      setMode('list'); load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const runNow = async (id: string) => {
    try {
      const r = await api.post(`/businesses/${businessId}/recurring-invoices/${id}/run-now`);
      Alert.alert('Done', r.data.generated > 0
        ? `Generated ${r.data.generated} invoice(s).` : 'Nothing due yet.');
      load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const togglePause = async (t: any) => {
    try {
      await api.post(`/businesses/${businessId}/recurring-invoices/${t.id}/pause`, {},
        { params: { paused: !t.paused } });
      load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  if (items === null) return <Screen><Loading /></Screen>;

  if (mode === 'create') {
    return (
      <Screen>
        <Text style={type.title}>New recurring template</Text>
        <Label>Customer</Label>
        <View style={{ flexDirection: 'row', flexWrap: 'wrap' }}>
          {customers.map(c => chip(customerId === c.id, c.name, () => setCustomerId(c.id), c.id))}
        </View>
        <Label>Template name</Label>
        <Input value={name} onChangeText={setName} placeholder="e.g. Acme monthly retainer" />
        <Label>Invoice number prefix</Label>
        <Input value={prefix} onChangeText={setPrefix} placeholder="REC" />
        <Label>Frequency</Label>
        <View style={{ flexDirection: 'row', flexWrap: 'wrap' }}>
          {FREQ.map((f, i) => chip(freq === i, f, () => setFreq(i), f))}
        </View>
        <Label>Payment terms (days)</Label>
        <Input value={terms} onChangeText={setTerms} keyboardType="number-pad" />
        <Label>First invoice date</Label>
        <Input value={nextRun} onChangeText={setNextRun} placeholder="YYYY-MM-DD" />
        <Label>Line description</Label>
        <Input value={desc} onChangeText={setDesc} placeholder="Defaults to the template name" />
        <Label>Amount (net)</Label>
        <Input value={price} onChangeText={setPrice} keyboardType="decimal-pad" placeholder="0.00" />
        <Label>VAT</Label>
        <View style={{ flexDirection: 'row' }}>
          {[[0, '20%'], [1, '5%'], [2, 'Zero'], [3, 'Exempt']].map(([v, l]) =>
            chip(vatRate === v, l as string, () => setVatRate(v as number), `v${v}`))}
        </View>
        <Label>Income account</Label>
        <View style={{ flexDirection: 'row', flexWrap: 'wrap' }}>
          {accounts.map(a => chip(accountId === a.id, `${a.code} ${a.name}`, () => setAccountId(a.id), a.id))}
        </View>
        <Label>When due</Label>
        <View style={{ flexDirection: 'row' }}>
          {chip(!autoPost, 'Create as draft', () => setAutoPost(false), 'd')}
          {chip(autoPost, 'Auto-post', () => setAutoPost(true), 'a')}
        </View>
        <Button title="Create template" onPress={create} disabled={!customerId || !name || !accountId} />
        <Button kind="ghost" title="Cancel" onPress={() => setMode('list')} />
      </Screen>
    );
  }

  return (
    <Screen>
      <Text style={[type.body, { color: colors.muted }]}>
        Templates generate sales invoices on schedule — retainers, rent, subscriptions.
        Drafts by default; auto-post if you trust the amount.
      </Text>
      {items.length === 0 ? <Empty text="No recurring templates yet." /> : items.map(t => (
        <View key={t.id} style={{ marginTop: spacing.s }}>
          <View style={{ flexDirection: 'row', alignItems: 'center' }}>
            <View style={{ flex: 1 }}>
              <LedgerRow left={t.name}
                sub={`${t.customer} · ${FREQ[t.frequency]} · next ${t.nextRunDate}${t.autoPost ? ' · auto-post' : ''}`}
                amount={`${t.generatedCount} sent`} />
            </View>
            {t.paused && <View style={{ marginLeft: 6 }}><Badge status="Paused" /></View>}
          </View>
          <View style={{ flexDirection: 'row', gap: spacing.s }}>
            <Button kind="ghost" title="Run now" onPress={() => runNow(t.id)} />
            <Button kind="ghost" title={t.paused ? 'Resume' : 'Pause'} onPress={() => togglePause(t)} />
          </View>
        </View>
      ))}
      <Button title="New recurring template" onPress={() => setMode('create')} />
    </Screen>
  );
}
