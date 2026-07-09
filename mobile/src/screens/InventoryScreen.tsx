import React, { useCallback, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Button, Empty, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, gbp, spacing, type } from '../theme';

const moveTypes = ['Purchase receipt', 'Sale issue', 'Issued to production', 'Production receipt', 'Adjustment'];

export default function InventoryScreen({ route }: any) {
  const { businessId } = route.params;
  const [data, setData] = useState<any | null>(null);
  const [movements, setMovements] = useState<any[] | null>(null);
  const [selectedItem, setSelectedItem] = useState<any | null>(null);
  const [adjusting, setAdjusting] = useState(false);
  const [qty, setQty] = useState('');
  const [unitCost, setUnitCost] = useState('');
  const [reason, setReason] = useState('');

  const load = useCallback(() => {
    // Idempotent: creates the manufacturing accounts on first visit for pre-v1.2 businesses.
    api.post(`/businesses/${businessId}/inventory/enable`).catch(() => {});
    api.get(`/businesses/${businessId}/inventory/levels`)
      .then(r => setData(r.data)).catch(e => Alert.alert('Error', errorMessage(e)));
  }, [businessId]);
  useFocusEffect(load);

  const openItem = async (item: any) => {
    setSelectedItem(item);
    setMovements(null);
    try {
      const r = await api.get(`/businesses/${businessId}/inventory/movements/${item.id}`);
      setMovements(r.data);
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const adjust = async () => {
    try {
      const r = await api.post(`/businesses/${businessId}/inventory/adjust`, {
        itemId: selectedItem.id, date: new Date().toISOString().slice(0, 10),
        quantity: parseFloat(qty) || 0,
        unitCost: unitCost ? parseFloat(unitCost) : null,
        reason: reason || 'Stock count adjustment',
      });
      Alert.alert('Adjusted', `Journal #${r.data.journalNumber} posted.`);
      setAdjusting(false); setQty(''); setUnitCost(''); setReason('');
      setSelectedItem(null);
      load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  if (selectedItem) {
    return (
      <Screen>
        <Text style={type.title}>{selectedItem.code} — {selectedItem.name}</Text>
        <View style={{ flexDirection: 'row', gap: spacing.l, marginVertical: spacing.s }}>
          <View>
            <Text style={type.label}>On hand</Text>
            <Text style={type.money}>{selectedItem.quantityOnHand}</Text>
          </View>
          <View>
            <Text style={type.label}>AVCO</Text>
            <Text style={type.money}>{gbp(selectedItem.avgUnitCost)}</Text>
          </View>
          <View>
            <Text style={type.label}>Value</Text>
            <Text style={type.money}>{gbp(selectedItem.value)}</Text>
          </View>
        </View>
        {adjusting ? (
          <>
            <Label>Quantity (+ writes up, − writes off)</Label>
            <Input value={qty} onChangeText={setQty} keyboardType="numbers-and-punctuation" placeholder="e.g. -3" />
            <Label>Unit cost (for increases; blank = current AVCO)</Label>
            <Input value={unitCost} onChangeText={setUnitCost} keyboardType="decimal-pad" />
            <Label>Reason</Label>
            <Input value={reason} onChangeText={setReason} placeholder="e.g. January count variance" />
            <Button title="Post adjustment" onPress={adjust} />
            <Button kind="ghost" title="Cancel" onPress={() => setAdjusting(false)} />
          </>
        ) : (
          <Button title="Adjust stock" onPress={() => setAdjusting(true)} />
        )}
        <Label>Item card (movements)</Label>
        {movements === null ? <Loading /> : movements.length === 0 ? <Empty text="No movements yet." /> :
          movements.map(m => (
            <LedgerRow key={m.id}
              left={moveTypes[m.type]}
              sub={`${m.date} · balance ${m.quantityAfter}${m.notes ? ` · ${m.notes}` : ''}`}
              amount={`${m.quantity > 0 ? '+' : ''}${m.quantity} @ ${gbp(m.unitCost)}`}
              amountColor={m.quantity > 0 ? colors.credit : colors.debit} />
          ))}
        <Button kind="ghost" title="Back to stock list" onPress={() => setSelectedItem(null)} />
      </Screen>
    );
  }

  return (
    <Screen>
      {data === null ? <Loading /> : (
        <>
          <View style={{
            backgroundColor: colors.card, borderRadius: 10, borderWidth: 1,
            borderColor: colors.rule, padding: spacing.m, marginBottom: spacing.m,
          }}>
            <Text style={type.label}>Total stock value (at AVCO)</Text>
            <Text style={[type.money, { fontSize: 22, marginTop: 4 }]}>{gbp(data.totalValue)}</Text>
          </View>
          {data.items.length === 0 ? (
            <Empty text="No stock-tracked items. Enable 'Track stock' on items in Products & Services — raw materials and finished goods default to tracked." />
          ) : data.items.map((i: any) => (
            <LedgerRow key={i.id} left={`${i.code}  ${i.name}`}
              sub={`${i.quantityOnHand} on hand @ ${gbp(i.avgUnitCost)} avg`}
              amount={gbp(i.value)} onPress={() => openItem(i)} />
          ))}
        </>
      )}
    </Screen>
  );
}
