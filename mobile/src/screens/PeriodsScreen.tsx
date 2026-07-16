import React, { useCallback, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Button, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, spacing, type } from '../theme';

export default function PeriodsScreen({ route }: any) {
  const { businessId } = route.params;
  const [status, setStatus] = useState<any | null>(null);
  const [lockDate, setLockDate] = useState('');
  const [yearEnd, setYearEnd] = useState('');

  const load = useCallback(() => {
    api.get(`/businesses/${businessId}/periods/status`)
      .then(r => { setStatus(r.data); setLockDate(r.data.lockedThrough ?? ''); })
      .catch(e => Alert.alert('Error', errorMessage(e)));
  }, [businessId]);
  useFocusEffect(load);

  const setLock = async (through: string | null) => {
    try {
      await api.put(`/businesses/${businessId}/periods/lock`, { through });
      Alert.alert(through ? 'Period locked' : 'Unlocked',
        through ? `Nothing can be posted on or before ${through}.` : 'The full history is editable again.');
      load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const closeYear = () => Alert.alert(
    'Close the year?',
    `This posts one journal transferring the year's profit or loss to Retained Earnings and locks everything up to ${yearEnd}. It can be reversed, but treat it as final.`,
    [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Close year', style: 'destructive',
        onPress: async () => {
          try {
            const r = await api.post(`/businesses/${businessId}/periods/close-year`, { yearEnd });
            Alert.alert('Year closed', `Journal #${r.data.journalNumber} posted. Locked through ${r.data.lockedThrough}.`);
            load();
          } catch (e) { Alert.alert('Error', errorMessage(e)); }
        },
      },
    ]);

  if (status === null) return <Screen><Loading /></Screen>;

  return (
    <Screen>
      <Text style={type.title}>Period lock</Text>
      <Text style={[type.body, { marginTop: spacing.xs }]}>
        {status.lockedThrough
          ? `Locked through ${status.lockedThrough} — nothing can be created, posted or reversed on or before that date.`
          : 'No lock set. Lock a period after filing its VAT return so the filed figures can never drift.'}
      </Text>
      <Label>Lock through (YYYY-MM-DD)</Label>
      <Input value={lockDate} onChangeText={setLockDate} placeholder="e.g. 2026-03-31" />
      <Button title="Set lock" onPress={() => setLock(lockDate)} disabled={!lockDate} />
      {status.lockedThrough && (
        <Button kind="ghost" title="Remove lock (Accountant/Owner)" onPress={() => setLock(null)} />
      )}

      <Text style={[type.title, { marginTop: spacing.xl }]}>Year-end close</Text>
      <Text style={[type.body, { marginTop: spacing.xs }]}>
        Transfers the year's profit or loss into Retained Earnings so the new year
        starts from zero, then locks the year. The P&L report for the closed year
        is unaffected — the closing journal is a transfer, not trading.
      </Text>
      {status.yearEndsClosed.length > 0 && (
        <View style={{ marginTop: spacing.s }}>
          {status.yearEndsClosed.map((c: any) => (
            <LedgerRow key={c.id} left={`Year ended ${c.date}`} amount={`journal #${c.number}`}
              amountColor={colors.credit} />
          ))}
        </View>
      )}
      <Label>Year end date (YYYY-MM-DD)</Label>
      <Input value={yearEnd} onChangeText={setYearEnd} placeholder="e.g. 2026-03-31" />
      <Button title="Close year" onPress={closeYear} disabled={!yearEnd} />
    </Screen>
  );
}
