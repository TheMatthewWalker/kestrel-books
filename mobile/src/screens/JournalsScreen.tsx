import React, { useCallback, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Badge, Button, Empty, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, gbp, spacing, type } from '../theme';

type NewLine = { accountId: string; side: 'dr' | 'cr'; amount: string };

export default function JournalsScreen({ route, navigation }: any) {
  const { businessId } = route.params;
  const [journals, setJournals] = useState<any[] | null>(null);
  const [detail, setDetail] = useState<any | null>(null);
  const [accounts, setAccounts] = useState<any[]>([]);
  const [adding, setAdding] = useState(false);
  const [narrative, setNarrative] = useState('');
  const [date, setDate] = useState(new Date().toISOString().slice(0, 10));
  const [newLines, setNewLines] = useState<NewLine[]>([
    { accountId: '', side: 'dr', amount: '' },
    { accountId: '', side: 'cr', amount: '' },
  ]);

  useFocusEffect(useCallback(() => {
    navigation.setOptions({ title: 'Journals' });
    api.get(`/businesses/${businessId}/journals`)
      .then(r => setJournals(r.data)).catch(e => Alert.alert('Error', errorMessage(e)));
    api.get(`/businesses/${businessId}/accounts`).then(r => setAccounts(r.data));
  }, [businessId]));

  const openDetail = async (id: string) => {
    try { setDetail((await api.get(`/businesses/${businessId}/journals/${id}`)).data); }
    catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const reverse = async () => {
    try {
      const r = await api.post(`/businesses/${businessId}/journals/${detail.id}/reverse`);
      Alert.alert('Reversed', `Reversal journal #${r.data.number} posted.`);
      setDetail(null);
      const list = await api.get(`/businesses/${businessId}/journals`);
      setJournals(list.data);
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const createAndPost = async () => {
    try {
      const lines = newLines
        .filter(l => l.accountId && parseFloat(l.amount) > 0)
        .map(l => ({
          accountId: l.accountId,
          debit: l.side === 'dr' ? parseFloat(l.amount) : 0,
          credit: l.side === 'cr' ? parseFloat(l.amount) : 0,
          description: narrative,
        }));
      const res = await api.post(`/businesses/${businessId}/journals`,
        { date, reference: 'MANUAL', narrative, lines });
      const posted = await api.post(`/businesses/${businessId}/journals/${res.data.id}/post`);
      Alert.alert('Posted', `Journal #${posted.data.number} posted.`);
      setAdding(false); setNarrative('');
      setNewLines([{ accountId: '', side: 'dr', amount: '' }, { accountId: '', side: 'cr', amount: '' }]);
      const list = await api.get(`/businesses/${businessId}/journals`);
      setJournals(list.data);
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  if (detail) {
    return (
      <Screen>
        <Text style={type.title}>Journal #{detail.number || '(draft)'}</Text>
        <Text style={[type.body, { marginTop: 4 }]}>{detail.narrative}</Text>
        <Text style={{ color: colors.muted, fontSize: 12, marginTop: 2 }}>{detail.date} · {detail.reference}</Text>
        <View style={{ marginVertical: spacing.s }}><Badge status={detail.status} /></View>
        <Label>Lines</Label>
        {detail.lines.map((l: any) => (
          <LedgerRow key={l.id}
            left={`${l.accountCode} ${l.accountName}`}
            sub={l.debit > 0 ? 'Dr' : 'Cr'}
            amount={gbp(l.debit > 0 ? l.debit : l.credit)}
            amountColor={l.debit > 0 ? colors.debit : colors.credit} />
        ))}
        {detail.status === 1 && <Button kind="danger" title="Reverse this journal" onPress={reverse} />}
        <Button kind="ghost" title="Back to list" onPress={() => setDetail(null)} />
      </Screen>
    );
  }

  const chip = (selected: boolean, label: string, onPress: () => void, key: string) => (
    <Text key={key} onPress={onPress}
      style={{
        paddingVertical: 4, paddingHorizontal: 10, borderRadius: 6, overflow: 'hidden', fontSize: 12,
        backgroundColor: selected ? colors.inkSoft : colors.badgeDraft,
        color: selected ? '#fff' : colors.ink,
      }}>{label}</Text>
  );

  return (
    <Screen>
      {!adding && <Button title="New manual journal" onPress={() => setAdding(true)} />}
      {adding && (
        <>
          <Label>Narrative</Label>
          <Input value={narrative} onChangeText={setNarrative} placeholder="e.g. Accrue electricity for June" />
          <Label>Date</Label>
          <Input value={date} onChangeText={setDate} placeholder="YYYY-MM-DD" />
          {newLines.map((l, idx) => (
            <View key={idx} style={{
              backgroundColor: '#fff', borderWidth: 1, borderColor: colors.rule,
              borderRadius: 8, padding: spacing.s, marginTop: spacing.s,
            }}>
              <View style={{ flexDirection: 'row', gap: spacing.s }}>
                {chip(l.side === 'dr', 'Debit', () => setNewLines(ls => ls.map((x, i) => i === idx ? { ...x, side: 'dr' } : x)), `dr${idx}`)}
                {chip(l.side === 'cr', 'Credit', () => setNewLines(ls => ls.map((x, i) => i === idx ? { ...x, side: 'cr' } : x)), `cr${idx}`)}
              </View>
              <Input style={{ marginTop: spacing.s }} value={l.amount} keyboardType="decimal-pad" placeholder="Amount"
                onChangeText={v => setNewLines(ls => ls.map((x, i) => i === idx ? { ...x, amount: v } : x))} />
              <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs, marginTop: spacing.s }}>
                {accounts.slice(0, 14).map((a: any) =>
                  chip(l.accountId === a.id, `${a.code}`, () =>
                    setNewLines(ls => ls.map((x, i) => i === idx ? { ...x, accountId: a.id } : x)), `${idx}-${a.id}`))}
              </View>
            </View>
          ))}
          <Button kind="ghost" title="Add line"
            onPress={() => setNewLines(ls => [...ls, { accountId: '', side: 'cr', amount: '' }])} />
          <Button title="Post journal" onPress={createAndPost} />
          <Button kind="ghost" title="Cancel" onPress={() => setAdding(false)} />
        </>
      )}
      <Label>Recent journals</Label>
      {journals === null ? <Loading /> : journals.length === 0 ? <Empty text="No journals yet." /> :
        journals.map(j => (
          <LedgerRow key={j.id}
            left={`#${j.number || '—'} ${j.narrative || j.reference}`}
            sub={`${j.date} · ${['Manual','Sales inv','Purchase inv','Receipt','Payment','Depreciation','Capitalisation','Reversal'][j.source]}`}
            amount={gbp(j.total)} onPress={() => openDetail(j.id)} />
        ))}
    </Screen>
  );
}
