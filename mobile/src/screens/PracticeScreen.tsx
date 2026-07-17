import React, { useCallback, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Button, Empty, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, gbp, spacing, type } from '../theme';

const KIND_LABEL = ['VAT return', 'Year end', 'Overdue debtors', 'VAT check'];

/** Cross-client "what's due" dashboard — the practice's morning screen. */
export default function PracticeScreen({ navigation }: any) {
  const [data, setData] = useState<any | null>(null);

  const load = useCallback(() => {
    api.get('/practice/dashboard', { params: { horizonDays: 60 } })
      .then(r => setData(r.data)).catch(e => Alert.alert('Error', errorMessage(e)));
  }, []);
  useFocusEffect(load);

  if (data === null) return <Screen><Loading /></Screen>;

  const urgency = (days: number) =>
    days < 0 ? colors.debit : days <= 7 ? colors.debit : days <= 21 ? colors.ink : colors.muted;

  return (
    <Screen>
      <Text style={[type.display, { fontSize: 24 }]}>Practice overview</Text>
      <Text style={[type.body, { color: colors.muted, marginBottom: spacing.m }]}>
        {data.clientCount} client{data.clientCount === 1 ? '' : 's'} · next 60 days
      </Text>

      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.s }}>
        <Stat label="Receivables" value={gbp(data.totalReceivables)} />
        <Stat label="Overdue" value={gbp(data.totalOverdue)} tone={data.totalOverdue > 0 ? colors.debit : undefined} />
        <Stat label="Payables" value={gbp(data.totalPayables)} />
        <Stat label="VAT due soon" value={String(data.vatReturnsDueSoon)}
          tone={data.vatReturnsDueSoon > 0 ? colors.debit : undefined} />
      </View>

      <Text style={[type.title, { marginTop: spacing.l }]}>Deadlines</Text>
      {data.deadlines.length === 0 ? <Empty text="Nothing due in the next 60 days. Rare and lovely." />
        : data.deadlines.map((d: any, idx: number) => (
          <LedgerRow key={idx}
            left={`${d.businessName} — ${d.title}`}
            sub={`${d.detail}${d.actioned ? ' · done' : ''}`}
            amount={d.kind === 2 ? 'chase' : d.actioned ? '✓' : `${d.daysUntil < 0 ? 'overdue' : d.daysUntil + 'd'}`}
            amountColor={d.actioned ? colors.credit : urgency(d.daysUntil)}
            onPress={() => navigation.navigate('Dashboard', { businessId: d.businessId, businessName: d.businessName })} />
        ))}
      <Text style={{ color: colors.muted, fontSize: 12, marginTop: spacing.s }}>
        VAT filing dates use the MTD rule (one month and seven days after the quarter end).
        Tap a line to open that client.
      </Text>
    </Screen>
  );
}

function Stat({ label, value, tone }: { label: string; value: string; tone?: string }) {
  return (
    <View style={{
      flexGrow: 1, minWidth: '45%', backgroundColor: '#fff', borderWidth: 1, borderColor: colors.rule,
      borderRadius: 10, padding: spacing.m,
    }}>
      <Text style={{ color: colors.muted, fontSize: 12 }}>{label}</Text>
      <Text style={{ fontSize: 20, fontWeight: '700', color: tone ?? colors.ink, marginTop: 2 }}>{value}</Text>
    </View>
  );
}
