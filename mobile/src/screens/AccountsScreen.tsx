import React, { useCallback, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Button, Empty, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, spacing, type } from '../theme';

const typeNames = ['Asset', 'Liability', 'Equity', 'Income', 'Expense'];

export default function AccountsScreen({ route }: any) {
  const { businessId } = route.params;
  const [accounts, setAccounts] = useState<any[] | null>(null);
  const [adding, setAdding] = useState(false);
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [accType, setAccType] = useState(0);

  const load = useCallback(() => {
    api.get(`/businesses/${businessId}/accounts`)
      .then(r => setAccounts(r.data)).catch(e => Alert.alert('Error', errorMessage(e)));
  }, [businessId]);
  useFocusEffect(load);

  const create = async () => {
    try {
      await api.post(`/businesses/${businessId}/accounts`, {
        code, name, type: accType, subType: null, isBank: false,
      });
      setAdding(false); setCode(''); setName(''); load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  let lastSub = '';
  return (
    <Screen>
      {accounts === null ? <Loading /> : accounts.length === 0 ? <Empty text="No accounts." /> :
        accounts.map(a => {
          const header = a.subType !== lastSub ? (lastSub = a.subType, (
            <Text key={`h-${a.subType}`} style={[type.label, { marginTop: spacing.m, marginBottom: spacing.xs }]}>
              {a.subType}
            </Text>
          )) : null;
          return (
            <View key={a.id}>
              {header}
              <LedgerRow left={`${a.code}  ${a.name}`}
                sub={a.isBank ? 'Bank account' : a.systemTag ? 'System account' : undefined} />
            </View>
          );
        })}
      {adding ? (
        <>
          <Label>Nominal code</Label>
          <Input value={code} onChangeText={setCode} placeholder="e.g. 7105" keyboardType="number-pad" />
          <Label>Name</Label>
          <Input value={name} onChangeText={setName} placeholder="e.g. Software Subscriptions" />
          <Label>Type</Label>
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs }}>
            {typeNames.map((t, i) => (
              <Text key={t}
                onPress={() => setAccType(i)}
                style={{
                  paddingVertical: 6, paddingHorizontal: 12, borderRadius: 6, overflow: 'hidden',
                  backgroundColor: accType === i ? colors.ink : colors.badgeDraft,
                  color: accType === i ? '#fff' : colors.ink, fontSize: 13, fontWeight: '600',
                }}>{t}</Text>
            ))}
          </View>
          <Button title="Add account" onPress={create} />
          <Button kind="ghost" title="Cancel" onPress={() => setAdding(false)} />
        </>
      ) : (
        <Button title="Add account" onPress={() => setAdding(true)} />
      )}
    </Screen>
  );
}
