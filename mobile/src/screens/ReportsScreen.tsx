import React, { useCallback, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Button, Empty, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, gbp, spacing, type } from '../theme';

const reports = [
  { key: 'trial-balance', label: 'Trial Balance', range: false },
  { key: 'profit-and-loss', label: 'Profit & Loss', range: true },
  { key: 'balance-sheet', label: 'SoFP', range: false },
  { key: 'cash-flow', label: 'Cash Flow', range: true },
];

export default function ReportsScreen({ route, navigation }: any) {
  const { businessId } = route.params;
  const [selected, setSelected] = useState('profit-and-loss');
  const [data, setData] = useState<any | null>(null);
  const [loading, setLoading] = useState(false);
  const today = new Date().toISOString().slice(0, 10);
  const [from, setFrom] = useState(`${new Date().getFullYear()}-01-01`);
  const [to, setTo] = useState(today);

  const run = useCallback(async () => {
    setLoading(true);
    try {
      const def = reports.find(r => r.key === selected)!;
      const params = def.range ? { from, to } : { asOf: to };
      const r = await api.get(`/businesses/${businessId}/reports/${selected}`, { params });
      setData(r.data);
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
    finally { setLoading(false); }
  }, [businessId, selected, from, to]);

  useFocusEffect(useCallback(() => {
    navigation.setOptions({ title: 'Reports' });
    run();
  }, [run]));

  const def = reports.find(r => r.key === selected)!;

  return (
    <Screen>
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs }}>
        {reports.map(r => (
          <Text key={r.key} onPress={() => { setSelected(r.key); setData(null); }}
            style={{
              paddingVertical: 6, paddingHorizontal: 12, borderRadius: 6, overflow: 'hidden', fontSize: 13,
              backgroundColor: selected === r.key ? colors.ink : colors.badgeDraft,
              color: selected === r.key ? '#fff' : colors.ink, fontWeight: '600',
            }}>{r.label}</Text>
        ))}
      </View>
      {def.range && (
        <>
          <Label>From</Label>
          <Input value={from} onChangeText={setFrom} placeholder="YYYY-MM-DD" />
        </>
      )}
      <Label>{def.range ? 'To' : 'As at'}</Label>
      <Input value={to} onChangeText={setTo} placeholder="YYYY-MM-DD" />
      <Button title="Run report" onPress={run} />

      {loading ? <Loading /> : data && (
        <View style={{ marginTop: spacing.m }}>
          <Text style={type.title}>{data.title}</Text>
          <Text style={{ color: colors.muted, fontSize: 12, marginBottom: spacing.s }}>{data.period}</Text>
          {data.sections.map((s: any) => (
            <View key={s.name} style={{ marginBottom: spacing.s }}>
              <Label>{s.name}</Label>
              {s.lines.length === 0 ? null : s.lines.map((l: any, i: number) => (
                <LedgerRow key={`${s.name}-${i}`} left={`${l.code !== '' && l.code !== '—' ? l.code + '  ' : ''}${l.name}`}
                  amount={gbp(l.amount)} />
              ))}
              <LedgerRow left={`Total ${s.name.toLowerCase()}`} amount={gbp(s.subtotal)}
                amountColor={colors.ink} />
            </View>
          ))}
          <View style={{ borderTopWidth: 2, borderTopColor: colors.ink, paddingTop: spacing.s }}>
            <LedgerRow
              left={selected === 'profit-and-loss' ? 'Net profit'
                : selected === 'cash-flow' ? 'Net cash movement'
                : selected === 'trial-balance' ? 'Difference (should be zero)'
                : 'Check (should be zero)'}
              amount={gbp(data.total)}
              amountColor={data.total >= 0 ? colors.credit : colors.debit} />
          </View>
          {data.sections.some((s: any) => s.name === 'Gross Profit') && (
            <Text style={{ color: colors.muted, fontSize: 12, marginTop: spacing.s }}>
              Gross profit = income − cost of sales. Net profit = gross profit − overheads.
            </Text>
          )}
        </View>
      )}
    </Screen>
  );
}
