import React, { useCallback, useState } from 'react';
import { Alert } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Button, Empty, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { Text, View } from 'react-native';
import { colors, gbp, spacing } from '../theme';

export default function ItemsScreen({ route }: any) {
  const { businessId } = route.params;
  const [items, setItems] = useState<any[] | null>(null);
  const [adding, setAdding] = useState(false);
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [price, setPrice] = useState('');
  const [kind, setKind] = useState(1);
  const [trackStock, setTrackStock] = useState(false);

  const load = useCallback(() => {
    api.get(`/businesses/${businessId}/items`)
      .then(r => setItems(r.data)).catch(e => Alert.alert('Error', errorMessage(e)));
  }, [businessId]);
  useFocusEffect(load);

  const create = async () => {
    try {
      await api.post(`/businesses/${businessId}/items`, {
        kind, code, name, salesPrice: parseFloat(price) || 0, purchasePrice: 0,
        defaultVatRate: 0, salesAccountId: null, purchaseAccountId: null,
        trackStock, inventoryAccountId: null, cogsAccountId: null,
      });
      setAdding(false); setCode(''); setName(''); setPrice(''); load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  return (
    <Screen>
      {items === null ? <Loading /> : items.length === 0 ? (
        <Empty text="No products or services yet." />
      ) : items.map(i => (
        <LedgerRow key={i.id} left={`${i.code}  ${i.name}`}
          sub={`${['Product', 'Service', 'Raw material', 'Finished good'][i.kind]}${i.trackStock ? ` · ${i.quantityOnHand} on hand` : ''}`} amount={gbp(i.salesPrice)} />
      ))}
      {adding ? (
        <>
          <Label>Code</Label>
          <Input value={code} onChangeText={setCode} placeholder="e.g. CONS-01" autoCapitalize="characters" />
          <Label>Name</Label>
          <Input value={name} onChangeText={setName} placeholder="e.g. Consulting (day rate)" />
          <Label>Kind</Label>
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs }}>
            {['Product', 'Service', 'Raw material', 'Finished good'].map((k, i) => (
              <Text key={k} onPress={() => { setKind(i); if (i === 2 || i === 3) setTrackStock(true); }}
                style={{ paddingVertical: 6, paddingHorizontal: 12, borderRadius: 6, overflow: 'hidden', fontSize: 13,
                  backgroundColor: kind === i ? colors.ink : colors.badgeDraft,
                  color: kind === i ? '#fff' : colors.ink, fontWeight: '600' }}>{k}</Text>
            ))}
          </View>
          <Label>Perpetual inventory</Label>
          <View style={{ flexDirection: 'row', gap: spacing.xs }}>
            <Text onPress={() => setTrackStock(!trackStock)}
              style={{ paddingVertical: 6, paddingHorizontal: 12, borderRadius: 6, overflow: 'hidden', fontSize: 13,
                backgroundColor: trackStock ? colors.credit : colors.badgeDraft,
                color: trackStock ? '#fff' : colors.ink, fontWeight: '600' }}>
              {trackStock ? 'Stock tracked — purchases go to stock, sales post COGS' : 'Not tracked — tap to enable'}
            </Text>
          </View>
          <Label>Sales price (net)</Label>
          <Input value={price} onChangeText={setPrice} keyboardType="decimal-pad" placeholder="0.00" />
          <Button title="Add item" onPress={create} />
          <Button kind="ghost" title="Cancel" onPress={() => setAdding(false)} />
        </>
      ) : (
        <Button title="Add product / service" onPress={() => setAdding(true)} />
      )}
    </Screen>
  );
}
