import React, { useCallback, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Button, Empty, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, spacing } from '../theme';

export default function ContactsScreen({ route }: any) {
  const { businessId } = route.params;
  const [tab, setTab] = useState<'customers' | 'vendors'>('customers');
  const [items, setItems] = useState<any[] | null>(null);
  const [adding, setAdding] = useState(false);
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');

  const load = useCallback(() => {
    setItems(null);
    api.get(`/businesses/${businessId}/${tab}`)
      .then(r => setItems(r.data)).catch(e => Alert.alert('Error', errorMessage(e)));
  }, [businessId, tab]);
  useFocusEffect(load);

  const create = async () => {
    try {
      await api.post(`/businesses/${businessId}/${tab}`, {
        name, email: email || null, phone: null, addressLine1: null, addressLine2: null,
        city: null, postcode: null, vatNumber: null, paymentTermsDays: 30, notes: null,
      });
      setAdding(false); setName(''); setEmail(''); load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  return (
    <Screen>
      <View style={{ flexDirection: 'row', gap: spacing.s, marginBottom: spacing.s }}>
        {(['customers', 'vendors'] as const).map(t => (
          <Text key={t} onPress={() => setTab(t)}
            style={{
              paddingVertical: 8, paddingHorizontal: 16, borderRadius: 8, overflow: 'hidden',
              backgroundColor: tab === t ? colors.ink : colors.badgeDraft,
              color: tab === t ? '#fff' : colors.ink, fontWeight: '600', fontSize: 14,
              textTransform: 'capitalize',
            }}>{t}</Text>
        ))}
      </View>
      {items === null ? <Loading /> : items.length === 0 ? (
        <Empty text={`No ${tab} yet.`} />
      ) : items.map(c => (
        <LedgerRow key={c.id} left={c.name} sub={c.email ?? undefined} />
      ))}
      {adding ? (
        <>
          <Label>Name</Label>
          <Input value={name} onChangeText={setName} placeholder="Name" autoFocus />
          <Label>Email (optional)</Label>
          <Input value={email} onChangeText={setEmail} autoCapitalize="none" keyboardType="email-address" />
          <Button title={`Add ${tab === 'customers' ? 'customer' : 'vendor'}`} onPress={create} />
          <Button kind="ghost" title="Cancel" onPress={() => setAdding(false)} />
        </>
      ) : (
        <Button title={`Add ${tab === 'customers' ? 'customer' : 'vendor'}`} onPress={() => setAdding(true)} />
      )}
    </Screen>
  );
}
