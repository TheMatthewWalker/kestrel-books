import React, { useCallback, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Button, Empty, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, gbp, spacing, type } from '../theme';

export default function AssetsScreen({ route }: any) {
  const { businessId } = route.params;
  const [assets, setAssets] = useState<any[] | null>(null);
  const [accounts, setAccounts] = useState<any[]>([]);
  const [adding, setAdding] = useState(false);

  const [code, setCode] = useState('');
  const [description, setDescription] = useState('');
  const [cost, setCost] = useState('');
  const [residual, setResidual] = useState('0');
  const [method, setMethod] = useState(0); // 0 SL, 1 RB
  const [lifeMonths, setLifeMonths] = useState('60');
  const [ratePct, setRatePct] = useState('25');
  const [underConstruction, setUnderConstruction] = useState(false);
  const [costAccId, setCostAccId] = useState('');
  const [accumAccId, setAccumAccId] = useState('');
  const [expAccId, setExpAccId] = useState('');

  const load = useCallback(() => {
    api.get(`/businesses/${businessId}/assets`)
      .then(r => setAssets(r.data)).catch(e => Alert.alert('Error', errorMessage(e)));
    api.get(`/businesses/${businessId}/accounts`).then(r => setAccounts(r.data));
  }, [businessId]);
  useFocusEffect(load);

  const fixedAssetAccounts = accounts.filter(a => a.subType === 'Fixed Assets' && !a.systemTag);
  const expenseAccounts = accounts.filter(a => a.type === 4 && a.code.startsWith('8'));

  const create = async () => {
    try {
      const today = new Date().toISOString().slice(0, 10);
      await api.post(`/businesses/${businessId}/assets`, {
        code, description, category: null,
        status: underConstruction ? 0 : 1,
        acquisitionDate: today,
        cost: parseFloat(cost) || 0,
        residualValue: parseFloat(residual) || 0,
        method,
        usefulLifeMonths: parseInt(lifeMonths) || 60,
        annualRatePercent: parseFloat(ratePct) || 0,
        depreciationStart: today,
        costAccountId: costAccId, accumDepAccountId: accumAccId, depExpenseAccountId: expAccId,
        notes: null,
      });
      setAdding(false); setCode(''); setDescription(''); setCost('');
      load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const runDepreciation = async () => {
    const now = new Date();
    try {
      const r = await api.post(
        `/businesses/${businessId}/assets/depreciation-run?year=${now.getFullYear()}&month=${now.getMonth() + 1}`);
      Alert.alert('Depreciation run',
        r.data.journalNumber ? `Journal #${r.data.journalNumber} posted.` : r.data.message);
      load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const capitalise = async (id: string) => {
    try {
      const today = new Date().toISOString().slice(0, 10);
      const r = await api.post(`/businesses/${businessId}/assets/${id}/capitalise?date=${today}`);
      Alert.alert('Capitalised', `Journal #${r.data.journalNumber} posted. Depreciation starts today.`);
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

  return (
    <Screen>
      <Button title="Run this month's depreciation" onPress={runDepreciation} />
      {assets === null ? <Loading /> : assets.length === 0 ? (
        <Empty text="No assets in the register yet." />
      ) : assets.map(a => (
        <View key={a.id}>
          <LedgerRow
            left={`${a.code}  ${a.description}`}
            sub={a.status === 0 ? 'Under construction'
              : `NBV after ${a.depreciatedThrough ?? 'no'} runs · charge ${gbp(a.nextMonthlyCharge)}/mo`}
            amount={gbp(a.status === 0 ? a.cost : a.netBookValue)} />
          {a.status === 0 && (
            <Button kind="ghost" title={`Capitalise ${a.code} into use`} onPress={() => capitalise(a.id)} />
          )}
        </View>
      ))}
      {adding ? (
        <>
          <Label>Asset code</Label>
          <Input value={code} onChangeText={setCode} placeholder="e.g. PM-001" autoCapitalize="characters" />
          <Label>Description</Label>
          <Input value={description} onChangeText={setDescription} placeholder="e.g. CNC milling machine" />
          <Label>Cost</Label>
          <Input value={cost} onChangeText={setCost} keyboardType="decimal-pad" placeholder="0.00" />
          <Label>Residual value</Label>
          <Input value={residual} onChangeText={setResidual} keyboardType="decimal-pad" />
          <Label>Status</Label>
          <View style={{ flexDirection: 'row', gap: spacing.s }}>
            {chip(!underConstruction, 'In use', () => setUnderConstruction(false), 'inuse')}
            {chip(underConstruction, 'Under construction (AUC)', () => setUnderConstruction(true), 'auc')}
          </View>
          <Label>Depreciation method</Label>
          <View style={{ flexDirection: 'row', gap: spacing.s }}>
            {chip(method === 0, 'Straight line', () => setMethod(0), 'sl')}
            {chip(method === 1, 'Reducing balance', () => setMethod(1), 'rb')}
          </View>
          {method === 0 ? (
            <>
              <Label>Useful life (months)</Label>
              <Input value={lifeMonths} onChangeText={setLifeMonths} keyboardType="number-pad" />
            </>
          ) : (
            <>
              <Label>Annual rate %</Label>
              <Input value={ratePct} onChangeText={setRatePct} keyboardType="decimal-pad" />
            </>
          )}
          <Label>Cost account</Label>
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs }}>
            {fixedAssetAccounts.filter(a => !a.name.includes('Depreciation')).map(a =>
              chip(costAccId === a.id, `${a.code} ${a.name.slice(0, 20)}`, () => setCostAccId(a.id), `c${a.id}`))}
          </View>
          <Label>Accumulated depreciation account</Label>
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs }}>
            {fixedAssetAccounts.filter(a => a.name.includes('Depreciation')).map(a =>
              chip(accumAccId === a.id, `${a.code} ${a.name.slice(0, 20)}`, () => setAccumAccId(a.id), `a${a.id}`))}
          </View>
          <Label>Depreciation expense account</Label>
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs }}>
            {expenseAccounts.map(a =>
              chip(expAccId === a.id, `${a.code} ${a.name.slice(0, 20)}`, () => setExpAccId(a.id), `e${a.id}`))}
          </View>
          <Button title="Add asset" onPress={create} />
          <Button kind="ghost" title="Cancel" onPress={() => setAdding(false)} />
        </>
      ) : (
        <Button kind="ghost" title="Add fixed asset" onPress={() => setAdding(true)} />
      )}
      <Text style={[type.body, { fontSize: 12, color: colors.muted, marginTop: spacing.m }]}>
        Straight line charges (cost − residual) ÷ useful life each month. Reducing balance charges
        NBV × annual rate ÷ 12. Runs post Dr depreciation expense / Cr accumulated depreciation
        and never depreciate below residual value.
      </Text>
    </Screen>
  );
}
