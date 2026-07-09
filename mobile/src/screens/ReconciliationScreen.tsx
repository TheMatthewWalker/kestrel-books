import React, { useCallback, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import * as DocumentPicker from 'expo-document-picker';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Button, Empty, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, gbp, spacing, type } from '../theme';

export default function ReconciliationScreen({ route }: any) {
  const { businessId } = route.params;
  const [banks, setBanks] = useState<any[]>([]);
  const [bankId, setBankId] = useState('');
  const [data, setData] = useState<any | null>(null);
  const [accounts, setAccounts] = useState<any[]>([]);
  const [busy, setBusy] = useState(false);
  const [creatingFor, setCreatingFor] = useState<string | null>(null);
  const [directAccountId, setDirectAccountId] = useState('');

  const loadLines = useCallback((accId: string) => {
    if (!accId) return;
    setData(null);
    api.get(`/businesses/${businessId}/banking/lines`, { params: { bankAccountId: accId } })
      .then(r => setData(r.data)).catch(e => Alert.alert('Error', errorMessage(e)));
  }, [businessId]);

  useFocusEffect(useCallback(() => {
    api.get(`/businesses/${businessId}/accounts`).then(r => {
      const bankList = r.data.filter((a: any) => a.isBank);
      setBanks(bankList);
      setAccounts(r.data.filter((a: any) => !a.isBank && !a.systemTag));
      const first = bankId || bankList[0]?.id;
      if (first) { setBankId(first); loadLines(first); }
    });
  }, [businessId]));

  const importFile = async () => {
    const res = await DocumentPicker.getDocumentAsync({
      type: ['text/csv', 'text/comma-separated-values', 'application/octet-stream', 'text/plain'],
      copyToCacheDirectory: true,
    });
    if (res.canceled || !res.assets?.length) return;
    const asset = res.assets[0];
    setBusy(true);
    try {
      const form = new FormData();
      form.append('file', { uri: asset.uri, name: asset.name, type: 'text/csv' } as any);
      const r = await api.post(
        `/businesses/${businessId}/banking/import?bankAccountId=${bankId}`, form,
        { headers: { 'Content-Type': 'multipart/form-data' } });
      Alert.alert('Imported', `${r.data.imported} new statement line(s).`);
      loadLines(bankId);
    } catch (e) { Alert.alert('Import failed', errorMessage(e)); }
    finally { setBusy(false); }
  };

  const match = async (lineId: string, journalLineId: string) => {
    try {
      await api.post(`/businesses/${businessId}/banking/lines/${lineId}/match/${journalLineId}`);
      loadLines(bankId);
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const exclude = async (lineId: string) => {
    try {
      await api.post(`/businesses/${businessId}/banking/lines/${lineId}/exclude`);
      loadLines(bankId);
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const createTx = async (lineId: string) => {
    if (!directAccountId) { Alert.alert('Pick an account', 'Choose which account the transaction analyses to.'); return; }
    try {
      const r = await api.post(`/businesses/${businessId}/banking/lines/${lineId}/create-transaction`,
        { directAccountId, salesInvoiceId: null, purchaseInvoiceId: null });
      Alert.alert('Posted', `Journal #${r.data.journalNumber} created and line reconciled.`);
      setCreatingFor(null); setDirectAccountId('');
      loadLines(bankId);
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const chip = (selected: boolean, label: string, onPress: () => void, key: string) => (
    <Text key={key} onPress={onPress}
      style={{
        paddingVertical: 4, paddingHorizontal: 10, borderRadius: 6, overflow: 'hidden', fontSize: 12,
        backgroundColor: selected ? colors.inkSoft : colors.badgeDraft,
        color: selected ? '#fff' : colors.ink,
      }}>{label}</Text>
  );

  const unmatched = data?.lines.filter((l: any) => l.status === 0) ?? [];
  const done = data?.lines.filter((l: any) => l.status !== 0) ?? [];

  return (
    <Screen>
      <Label>Bank account</Label>
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs }}>
        {banks.map(b => chip(bankId === b.id, `${b.code} ${b.name}`,
          () => { setBankId(b.id); loadLines(b.id); }, b.id))}
      </View>
      <Button title={busy ? 'Importing…' : 'Import statement (CSV / OFX)'} onPress={importFile} disabled={busy || !bankId} />

      {data && (
        <Text style={[type.title, { marginTop: spacing.m }]}>
          {data.progress.reconciled} of {data.progress.total} lines reconciled
        </Text>
      )}

      <Label>To reconcile</Label>
      {data === null ? <Loading /> : unmatched.length === 0 ? (
        <Empty text="Nothing outstanding — import a statement or you're fully reconciled." />
      ) : unmatched.map((l: any) => (
        <View key={l.id} style={{
          backgroundColor: '#fff', borderWidth: 1, borderColor: colors.rule,
          borderRadius: 8, padding: spacing.s, marginBottom: spacing.s,
        }}>
          <LedgerRow left={l.description || '(no description)'} sub={l.date}
            amount={gbp(Math.abs(l.amount))}
            amountColor={l.amount > 0 ? colors.credit : colors.debit} />
          {l.suggestions.length > 0 && (
            <>
              <Text style={{ fontSize: 12, color: colors.muted, marginTop: spacing.xs }}>Suggested matches:</Text>
              {l.suggestions.map((s: any) => (
                <View key={s.journalLineId} style={{ flexDirection: 'row', alignItems: 'center', marginTop: spacing.xs }}>
                  <Text style={{ flex: 1, fontSize: 12, color: colors.inkSoft }} numberOfLines={1}>
                    #{s.journalNumber} · {s.date} · {s.narrative}
                  </Text>
                  {chip(false, 'Match', () => match(l.id, s.journalLineId), `m${s.journalLineId}`)}
                </View>
              ))}
            </>
          )}
          {creatingFor === l.id ? (
            <>
              <Text style={{ fontSize: 12, color: colors.muted, marginTop: spacing.xs }}>
                Post {l.amount > 0 ? 'money in' : 'money out'} of {gbp(Math.abs(l.amount))} to:
              </Text>
              <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs, marginTop: spacing.xs }}>
                {accounts.slice(0, 12).map((a: any) =>
                  chip(directAccountId === a.id, `${a.code} ${a.name.slice(0, 16)}`,
                    () => setDirectAccountId(a.id), `${l.id}-${a.id}`))}
              </View>
              <View style={{ flexDirection: 'row', gap: spacing.s, marginTop: spacing.s }}>
                {chip(false, 'Post & reconcile', () => createTx(l.id), `go${l.id}`)}
                {chip(false, 'Cancel', () => setCreatingFor(null), `x${l.id}`)}
              </View>
            </>
          ) : (
            <View style={{ flexDirection: 'row', gap: spacing.s, marginTop: spacing.xs }}>
              {chip(false, 'Create transaction', () => { setCreatingFor(l.id); setDirectAccountId(''); }, `c${l.id}`)}
              {chip(false, 'Exclude', () => exclude(l.id), `e${l.id}`)}
            </View>
          )}
        </View>
      ))}

      {done.length > 0 && (
        <>
          <Label>Reconciled / excluded</Label>
          {done.slice(0, 30).map((l: any) => (
            <LedgerRow key={l.id} left={l.description || '(no description)'}
              sub={`${l.date} · ${l.status === 1 ? 'Matched' : 'Excluded'}`}
              amount={gbp(Math.abs(l.amount))} />
          ))}
        </>
      )}
    </Screen>
  );
}
