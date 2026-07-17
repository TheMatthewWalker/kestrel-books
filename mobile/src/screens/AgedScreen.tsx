import React, { useCallback, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Button, Empty, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, gbp, spacing, type } from '../theme';

/** Aged debtors/creditors with drill-down to an open-item customer statement. */
export default function AgedScreen({ route, navigation }: any) {
  const { businessId, kind } = route.params; // 'debtors' | 'creditors'
  const [report, setReport] = useState<any | null>(null);
  const [statement, setStatement] = useState<any | null>(null);

  const load = useCallback(() => {
    navigation.setOptions({ title: kind === 'debtors' ? 'Aged Debtors' : 'Aged Creditors' });
    api.get(`/businesses/${businessId}/reports/aged-${kind}`)
      .then(r => setReport(r.data)).catch(e => Alert.alert('Error', errorMessage(e)));
  }, [businessId, kind]);
  useFocusEffect(load);

  const openStatement = async (contactId: string) => {
    try {
      const r = await api.get(`/businesses/${businessId}/reports/customer-statement/${contactId}`);
      setStatement(r.data);
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  if (report === null) return <Screen><Loading /></Screen>;

  if (statement) {
    return (
      <Screen>
        <Text style={type.title}>{statement.contactName}</Text>
        <Text style={[type.body, { color: colors.muted }]}>
          Statement of account as at {statement.asOf} — open items
        </Text>
        <View style={{ marginTop: spacing.m }}>
          {statement.items.map((i: any, idx: number) => (
            <LedgerRow key={idx}
              left={`${i.kind} ${i.number}`}
              sub={`${i.date} · due ${i.dueDate}${i.daysOverdue > 0 ? ` · ${i.daysOverdue} days overdue` : ''}`}
              amount={gbp(i.outstanding)}
              amountColor={i.outstanding < 0 ? colors.credit : i.daysOverdue > 30 ? colors.debit : colors.ink} />
          ))}
        </View>
        <LedgerRow left="Total due" amount={gbp(statement.totalDue)} amountColor={colors.debit} />
        <Text style={{ color: colors.muted, fontSize: 12, marginTop: spacing.s }}>
          PDF statements for emailing arrive with invoice PDFs (roadmap 4.6).
        </Text>
        <Button kind="ghost" title="Back to ageing" onPress={() => setStatement(null)} />
      </Screen>
    );
  }

  const B = report.totals;
  const bucketRow = (label: string, v: number, warn = false) => (
    <LedgerRow key={label} left={label} amount={gbp(v)}
      amountColor={warn && v > 0 ? colors.debit : colors.ink} />
  );

  return (
    <Screen>
      <Text style={[type.body, { color: colors.muted }]}>As at {report.asOf} · by days overdue</Text>
      <View style={{ marginTop: spacing.s }}>
        {bucketRow('Current (not yet due)', B.current)}
        {bucketRow('1–30 days', B.days30)}
        {bucketRow('31–60 days', B.days60, true)}
        {bucketRow('61–90 days', B.days90, true)}
        {bucketRow('90+ days', B.older, true)}
        <LedgerRow left="Total" amount={gbp(B.total)} amountColor={colors.ink} />
      </View>

      <Text style={[type.title, { marginTop: spacing.l }]}>
        By {kind === 'debtors' ? 'customer' : 'supplier'}
      </Text>
      {report.rows.length === 0 ? <Empty text="Nothing outstanding — enviable." /> : report.rows.map((r: any) => (
        <LedgerRow key={r.contactId} left={r.name}
          sub={`90+: ${gbp(r.buckets.older)} · 61–90: ${gbp(r.buckets.days90)} · 31–60: ${gbp(r.buckets.days60)}`}
          amount={gbp(r.buckets.total)}
          amountColor={r.buckets.older > 0 ? colors.debit : colors.ink}
          onPress={kind === 'debtors' ? () => openStatement(r.contactId) : undefined} />
      ))}
      {kind === 'debtors' && <Text style={{ color: colors.muted, fontSize: 12, marginTop: spacing.s }}>
        Tap a customer for their open-item statement.
      </Text>}
    </Screen>
  );
}
