import React, { useCallback, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api } from '../api';
import { Screen } from '../components/ui';
import { colors, gbp, spacing, type } from '../theme';

const tiles = [
  { title: 'Scan receipt', route: 'ReceiptScan' },
  { title: 'Bank reconciliation', route: 'Reconciliation' },
  { title: 'Sales invoices', route: 'Invoices', params: { kind: 'sales' } },
  { title: 'Purchase invoices', route: 'Invoices', params: { kind: 'purchase' } },
  { title: 'Money in / out', route: 'Money' },
  { title: 'Journals', route: 'Journals' },
  { title: 'Reports', route: 'Reports' },
  { title: 'Fixed assets', route: 'Assets' },
  { title: 'Inventory', route: 'Inventory' },
  { title: 'Production', route: 'Production' },
  { title: 'Customers & vendors', route: 'Contacts' },
  { title: 'Products & services', route: 'Items' },
  { title: 'Chart of accounts', route: 'Accounts' },
  { title: 'HMRC / MTD', route: 'Mtd' },
  { title: 'Sales credit notes', route: 'CreditNotes', params: { kind: 'sales' } },
  { title: 'Purchase credit notes', route: 'CreditNotes', params: { kind: 'purchase' } },
  { title: 'Opening balances', route: 'Opening' },
  { title: 'Aged debtors', route: 'Aged', params: { kind: 'debtors' } },
  { title: 'Aged creditors', route: 'Aged', params: { kind: 'creditors' } },
  { title: 'Periods & year end', route: 'Periods' },
];

export default function DashboardScreen({ route, navigation }: any) {
  const { businessId, businessName } = route.params;
  const [cash, setCash] = useState<number | null>(null);
  const [profit, setProfit] = useState<number | null>(null);

  useFocusEffect(useCallback(() => {
    navigation.setOptions({ title: businessName });
    const today = new Date();
    const yearStart = `${today.getFullYear()}-01-01`;
    const iso = today.toISOString().slice(0, 10);
    api.get(`/businesses/${businessId}/reports/balance-sheet`, { params: { asOf: iso } })
      .then(r => {
        const assets = r.data.sections.find((s: any) => s.name === 'Assets');
        const bank = assets?.lines
          .filter((l: any) => l.code.startsWith('12'))
          .reduce((a: number, l: any) => a + l.amount, 0) ?? 0;
        setCash(bank);
      }).catch(() => setCash(0));
    api.get(`/businesses/${businessId}/reports/profit-and-loss`, { params: { from: yearStart, to: iso } })
      .then(r => setProfit(r.data.total)).catch(() => setProfit(0));
  }, [businessId]));

  return (
    <Screen>
      <View style={styles.summary}>
        <View style={{ flex: 1 }}>
          <Text style={type.label}>Cash at bank</Text>
          <Text style={[type.money, styles.figure]}>{cash === null ? '—' : gbp(cash)}</Text>
        </View>
        <View style={{ flex: 1 }}>
          <Text style={type.label}>Profit YTD</Text>
          <Text style={[type.money, styles.figure, { color: (profit ?? 0) >= 0 ? colors.credit : colors.debit }]}>
            {profit === null ? '—' : gbp(profit)}
          </Text>
        </View>
      </View>
      <View style={styles.grid}>
        {tiles.map(t => (
          <Pressable key={t.title} style={({ pressed }) => [styles.tile, pressed && { opacity: 0.7 }]}
            onPress={() => navigation.navigate(t.route, { businessId, ...(t.params ?? {}) })}>
            <Text style={type.title}>{t.title}</Text>
          </Pressable>
        ))}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  summary: {
    flexDirection: 'row', backgroundColor: colors.card, borderRadius: 10,
    borderWidth: 1, borderColor: colors.rule, padding: spacing.m, marginBottom: spacing.m,
  },
  figure: { fontSize: 20, marginTop: 4 },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.s },
  tile: {
    width: '48%', backgroundColor: colors.card, borderRadius: 10,
    borderWidth: 1, borderColor: colors.rule, padding: spacing.m, minHeight: 76, justifyContent: 'center',
  },
});
