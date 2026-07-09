import React, { useCallback, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Button, Empty, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, gbp, spacing, type } from '../theme';

const statusNames = ['Draft', 'In progress', 'Completed', 'Cancelled'];

export default function ProductionScreen({ route }: any) {
  const { businessId } = route.params;
  const [orders, setOrders] = useState<any[] | null>(null);
  const [items, setItems] = useState<any[]>([]);
  const [view, setView] = useState<'orders' | 'newOrder' | 'bom'>('orders');

  // New order
  const [orderItemId, setOrderItemId] = useState('');
  const [orderQty, setOrderQty] = useState('');

  // Completion
  const [completingId, setCompletingId] = useState<string | null>(null);
  const [completeQty, setCompleteQty] = useState('');

  // BOM editor
  const [bomItemId, setBomItemId] = useState('');
  const [labour, setLabour] = useState('0');
  const [overhead, setOverhead] = useState('0');
  const [bomLines, setBomLines] = useState<{ componentItemId: string; quantityPer: string }[]>([]);

  const load = useCallback(() => {
    api.get(`/businesses/${businessId}/production/orders`)
      .then(r => setOrders(r.data)).catch(e => Alert.alert('Error', errorMessage(e)));
    api.get(`/businesses/${businessId}/items`).then(r => setItems(r.data));
  }, [businessId]);
  useFocusEffect(load);

  const tracked = items.filter((i: any) => i.trackStock);
  const finishedGoods = tracked.filter((i: any) => i.kind === 3 || i.kind === 0);
  const components = tracked.filter((i: any) => i.kind === 2 || i.kind === 0);

  const loadBom = async (itemId: string) => {
    setBomItemId(itemId);
    try {
      const r = await api.get(`/businesses/${businessId}/production/boms/${itemId}`);
      if (r.data.exists) {
        setLabour(String(r.data.labourCostPerUnit));
        setOverhead(String(r.data.overheadCostPerUnit));
        setBomLines(r.data.lines.map((l: any) => ({
          componentItemId: l.componentItemId, quantityPer: String(l.quantityPer),
        })));
      } else {
        setLabour('0'); setOverhead('0'); setBomLines([]);
      }
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const saveBom = async () => {
    try {
      await api.put(`/businesses/${businessId}/production/boms/${bomItemId}`, {
        labourCostPerUnit: parseFloat(labour) || 0,
        overheadCostPerUnit: parseFloat(overhead) || 0,
        lines: bomLines
          .filter(l => l.componentItemId && parseFloat(l.quantityPer) > 0)
          .map(l => ({ componentItemId: l.componentItemId, quantityPer: parseFloat(l.quantityPer) })),
      });
      Alert.alert('Saved', 'Bill of materials updated.');
      setView('orders');
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const createOrder = async () => {
    try {
      const r = await api.post(`/businesses/${businessId}/production/orders`, {
        itemId: orderItemId, quantity: parseFloat(orderQty) || 0, notes: null,
      });
      Alert.alert('Created', `Works order ${r.data.number} raised.`);
      setView('orders'); setOrderQty('');
      load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const issue = async (id: string) => {
    try {
      const r = await api.post(`/businesses/${businessId}/production/orders/${id}/issue-materials`);
      Alert.alert('Materials issued', `Journal #${r.data.journalNumber}: Dr WIP / Cr raw materials at AVCO.`);
      load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const complete = async (id: string) => {
    try {
      const r = await api.post(`/businesses/${businessId}/production/orders/${id}/complete`, {
        date: new Date().toISOString().slice(0, 10),
        quantityCompleted: parseFloat(completeQty) || 0,
      });
      Alert.alert('Completed', `Journal #${r.data.journalNumber}: labour & overhead absorbed, WIP transferred to finished goods.`);
      setCompletingId(null); setCompleteQty('');
      load();
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

  if (view === 'bom') {
    return (
      <Screen>
        <Label>Finished good</Label>
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs }}>
          {finishedGoods.map((i: any) => chip(bomItemId === i.id, `${i.code} ${i.name.slice(0, 16)}`,
            () => loadBom(i.id), i.id))}
        </View>
        {bomItemId !== '' && (
          <>
            <Label>Components (per unit produced)</Label>
            {bomLines.map((l, idx) => (
              <View key={idx} style={{
                backgroundColor: '#fff', borderWidth: 1, borderColor: colors.rule,
                borderRadius: 8, padding: spacing.s, marginBottom: spacing.s,
              }}>
                <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs }}>
                  {components.filter((c: any) => c.id !== bomItemId).map((c: any) =>
                    chip(l.componentItemId === c.id, `${c.code}`, () =>
                      setBomLines(ls => ls.map((x, i) => i === idx ? { ...x, componentItemId: c.id } : x)),
                      `${idx}-${c.id}`))}
                </View>
                <Input style={{ marginTop: spacing.s }} value={l.quantityPer} keyboardType="decimal-pad"
                  placeholder="Quantity per unit"
                  onChangeText={v => setBomLines(ls => ls.map((x, i) => i === idx ? { ...x, quantityPer: v } : x))} />
              </View>
            ))}
            <Button kind="ghost" title="Add component"
              onPress={() => setBomLines(ls => [...ls, { componentItemId: '', quantityPer: '' }])} />
            <Label>Labour cost per unit</Label>
            <Input value={labour} onChangeText={setLabour} keyboardType="decimal-pad" />
            <Label>Overhead absorbed per unit</Label>
            <Input value={overhead} onChangeText={setOverhead} keyboardType="decimal-pad" />
            <Button title="Save BOM" onPress={saveBom} />
          </>
        )}
        <Button kind="ghost" title="Back" onPress={() => setView('orders')} />
      </Screen>
    );
  }

  if (view === 'newOrder') {
    return (
      <Screen>
        <Label>Item to manufacture</Label>
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs }}>
          {finishedGoods.map((i: any) => chip(orderItemId === i.id, `${i.code} ${i.name.slice(0, 16)}`,
            () => setOrderItemId(i.id), i.id))}
        </View>
        <Label>Quantity</Label>
        <Input value={orderQty} onChangeText={setOrderQty} keyboardType="decimal-pad" placeholder="e.g. 100" />
        <Button title="Raise works order" onPress={createOrder} />
        <Button kind="ghost" title="Back" onPress={() => setView('orders')} />
      </Screen>
    );
  }

  return (
    <Screen>
      <View style={{ flexDirection: 'row', gap: spacing.s }}>
        <Button title="New works order" onPress={() => setView('newOrder')} />
      </View>
      <Button kind="ghost" title="Bills of materials" onPress={() => setView('bom')} />
      <Label>Works orders</Label>
      {orders === null ? <Loading /> : orders.length === 0 ? (
        <Empty text="No works orders yet. Set up a bill of materials, then raise an order." />
      ) : orders.map((o: any) => (
        <View key={o.id} style={{
          backgroundColor: '#fff', borderWidth: 1, borderColor: colors.rule,
          borderRadius: 8, padding: spacing.s, marginBottom: spacing.s,
        }}>
          <LedgerRow left={`${o.number} — ${o.itemCode} × ${o.quantityPlanned}`}
            sub={`${statusNames[o.status]} · materials ${gbp(o.materialCost)} · labour ${gbp(o.labourCost)} · o/h ${gbp(o.overheadCost)}`}
            amount={gbp(o.totalCost)} />
          {o.status === 0 && (
            <View style={{ flexDirection: 'row', gap: spacing.s, marginTop: spacing.xs }}>
              {chip(false, 'Issue materials (Dr WIP / Cr RM)', () => issue(o.id), `i${o.id}`)}
            </View>
          )}
          {o.status === 1 && (completingId === o.id ? (
            <>
              <Input style={{ marginTop: spacing.s }} value={completeQty} keyboardType="decimal-pad"
                placeholder={`Quantity completed (planned ${o.quantityPlanned})`}
                onChangeText={setCompleteQty} />
              <View style={{ flexDirection: 'row', gap: spacing.s, marginTop: spacing.xs }}>
                {chip(false, 'Complete order', () => complete(o.id), `go${o.id}`)}
                {chip(false, 'Cancel', () => setCompletingId(null), `x${o.id}`)}
              </View>
            </>
          ) : (
            <View style={{ flexDirection: 'row', gap: spacing.s, marginTop: spacing.xs }}>
              {chip(false, 'Complete → finished goods', () => { setCompletingId(o.id); setCompleteQty(String(o.quantityPlanned)); }, `c${o.id}`)}
            </View>
          ))}
        </View>
      ))}
      <Text style={{ color: colors.muted, fontSize: 12, marginTop: spacing.s }}>
        Cost flow: raw materials issue into WIP at AVCO; completion absorbs labour and overhead
        into WIP, then transfers the full order cost into finished goods stock. Selling the
        finished item posts cost of goods sold automatically.
      </Text>
    </Screen>
  );
}
