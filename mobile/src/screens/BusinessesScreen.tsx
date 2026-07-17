import React, { useCallback, useState } from 'react';
import { Alert, Text } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { useAuth } from '../auth';
import { Button, Empty, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { type, spacing } from '../theme';

export default function BusinessesScreen({ navigation }: any) {
  const { signOut, displayName } = useAuth();
  const [items, setItems] = useState<any[] | null>(null);
  const [name, setName] = useState('');
  const [adding, setAdding] = useState(false);

  const load = useCallback(() => {
    api.get('/businesses').then(r => setItems(r.data)).catch(e => Alert.alert('Error', errorMessage(e)));
  }, []);
  useFocusEffect(load);

  const create = async () => {
    if (!name.trim()) return;
    try {
      await api.post('/businesses', { name: name.trim() });
      setName(''); setAdding(false); load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  return (
    <Screen>
      <Button title="Practice overview — what's due" onPress={() => navigation.navigate('Practice')} />
      <Text style={[type.body, { marginBottom: spacing.s }]}>Signed in as {displayName}</Text>
      {items === null ? <Loading /> : items.length === 0 ? (
        <Empty text="No clients yet. Add your first business — a full UK chart of accounts is created automatically." />
      ) : items.map(b => (
        <LedgerRow key={b.id} left={b.name} sub={b.vatNumber ? `VAT ${b.vatNumber}` : undefined}
          onPress={() => navigation.navigate('Dashboard', { businessId: b.id, businessName: b.name })} />
      ))}
      {adding ? (
        <>
          <Label>Business name</Label>
          <Input value={name} onChangeText={setName} placeholder="Acme Widgets Ltd" autoFocus />
          <Button title="Create client" onPress={create} />
          <Button kind="ghost" title="Cancel" onPress={() => setAdding(false)} />
        </>
      ) : (
        <Button title="Add client business" onPress={() => setAdding(true)} />
      )}
      <Button kind="ghost" title="Account security (MFA)" onPress={() => navigation.navigate('Security')} />
      <Button kind="ghost" title="Sign out" onPress={signOut} />
    </Screen>
  );
}
