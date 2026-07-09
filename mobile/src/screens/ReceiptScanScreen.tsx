import React, { useCallback, useState } from 'react';
import { Alert, Image, Text, View } from 'react-native';
import * as ImagePicker from 'expo-image-picker';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Button, Empty, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, gbp, spacing, type } from '../theme';

export default function ReceiptScanScreen({ route, navigation }: any) {
  const { businessId } = route.params;
  const [scans, setScans] = useState<any[] | null>(null);
  const [accounts, setAccounts] = useState<any[]>([]);
  const [banks, setBanks] = useState<any[]>([]);
  const [busy, setBusy] = useState(false);

  // Confirmation form state for the scan being reviewed
  const [current, setCurrent] = useState<any | null>(null);
  const [vendor, setVendor] = useState('');
  const [date, setDate] = useState('');
  const [net, setNet] = useState('');
  const [vat, setVat] = useState('');
  const [expenseAccId, setExpenseAccId] = useState('');
  const [mode, setMode] = useState<'invoice' | 'money'>('money');
  const [bankId, setBankId] = useState('');

  const load = useCallback(() => {
    api.get(`/businesses/${businessId}/receipts`).then(r => setScans(r.data)).catch(() => setScans([]));
    api.get(`/businesses/${businessId}/accounts`).then(r => {
      setAccounts(r.data.filter((a: any) => a.type === 4));
      const bankList = r.data.filter((a: any) => a.isBank);
      setBanks(bankList);
      if (bankList.length && !bankId) setBankId(bankList[0].id);
    });
  }, [businessId]);
  useFocusEffect(load);

  const beginReview = (scan: any) => {
    setCurrent(scan);
    setVendor(scan.vendorName ?? '');
    setDate(scan.receiptDate ?? new Date().toISOString().slice(0, 10));
    setNet(scan.netAmount != null ? String(scan.netAmount)
      : scan.grossAmount != null ? (scan.grossAmount / 1.2).toFixed(2) : '');
    setVat(scan.vatAmount != null ? String(scan.vatAmount)
      : scan.grossAmount != null ? (scan.grossAmount - scan.grossAmount / 1.2).toFixed(2) : '');
  };

  const capture = async (fromCamera: boolean) => {
    const perm = fromCamera
      ? await ImagePicker.requestCameraPermissionsAsync()
      : await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (!perm.granted) { Alert.alert('Permission needed', 'Allow camera/photo access in Settings.'); return; }

    const result = fromCamera
      ? await ImagePicker.launchCameraAsync({ quality: 0.7 })
      : await ImagePicker.launchImageLibraryAsync({ quality: 0.7 });
    if (result.canceled || !result.assets?.length) return;

    const asset = result.assets[0];
    setBusy(true);
    try {
      const form = new FormData();
      form.append('file', {
        uri: asset.uri, name: 'receipt.jpg', type: 'image/jpeg',
      } as any);
      const r = await api.post(`/businesses/${businessId}/receipts/upload`, form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      });
      beginReview(r.data);
      load();
    } catch (e) { Alert.alert('Upload failed', errorMessage(e)); }
    finally { setBusy(false); }
  };

  const confirm = async () => {
    try {
      await api.post(`/businesses/${businessId}/receipts/${current.id}/confirm`, {
        vendorName: vendor, date, net: parseFloat(net) || 0, vat: parseFloat(vat) || 0,
        expenseAccountId: expenseAccId, mode, bankAccountId: mode === 'money' ? bankId : null,
      });
      Alert.alert('Done', mode === 'money'
        ? 'Posted as money out with the VAT split to Input VAT.'
        : 'Draft purchase invoice created — post it from Purchase Invoices.');
      setCurrent(null);
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

  if (current) {
    const gross = (parseFloat(net) || 0) + (parseFloat(vat) || 0);
    return (
      <Screen>
        <Image source={{ uri: `${api.defaults.baseURL}/businesses/${businessId}/receipts/${current.id}/image` }}
          style={{ height: 180, borderRadius: 8, backgroundColor: colors.rule }} resizeMode="contain" />
        {current.extractionNotes ? (
          <Text style={{ color: colors.muted, fontSize: 12, marginTop: spacing.s }}>{current.extractionNotes}</Text>
        ) : null}
        <Label>Vendor</Label>
        <Input value={vendor} onChangeText={setVendor} placeholder="e.g. Screwfix" />
        <Label>Date</Label>
        <Input value={date} onChangeText={setDate} placeholder="YYYY-MM-DD" />
        <View style={{ flexDirection: 'row', gap: spacing.s }}>
          <View style={{ flex: 1 }}>
            <Label>Net</Label>
            <Input value={net} onChangeText={setNet} keyboardType="decimal-pad" />
          </View>
          <View style={{ flex: 1 }}>
            <Label>VAT</Label>
            <Input value={vat} onChangeText={setVat} keyboardType="decimal-pad" />
          </View>
        </View>
        <Text style={[type.money, { marginTop: spacing.s }]}>Gross {gbp(gross)}</Text>
        <Label>Expense account</Label>
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs }}>
          {accounts.slice(0, 12).map(a =>
            chip(expenseAccId === a.id, `${a.code} ${a.name.slice(0, 18)}`, () => setExpenseAccId(a.id), a.id))}
        </View>
        <Label>How was it paid?</Label>
        <View style={{ flexDirection: 'row', gap: spacing.s }}>
          {chip(mode === 'money', 'Paid on the spot (money out)', () => setMode('money'), 'money')}
          {chip(mode === 'invoice', 'On account (purchase invoice)', () => setMode('invoice'), 'invoice')}
        </View>
        {mode === 'money' && (
          <>
            <Label>Paid from</Label>
            <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs }}>
              {banks.map(b => chip(bankId === b.id, `${b.code} ${b.name}`, () => setBankId(b.id), b.id))}
            </View>
          </>
        )}
        <Button title="Confirm & post" onPress={confirm} />
        <Button kind="ghost" title="Back" onPress={() => setCurrent(null)} />
      </Screen>
    );
  }

  return (
    <Screen>
      <Button title={busy ? 'Uploading…' : 'Photograph a receipt'} onPress={() => capture(true)} disabled={busy} />
      <Button kind="ghost" title="Pick from photo library" onPress={() => capture(false)} disabled={busy} />
      <Text style={{ color: colors.muted, fontSize: 12, marginTop: spacing.s }}>
        The photo is kept as the source document. With an Anthropic API key configured on the
        server, vendor, date and amounts are extracted automatically; otherwise key them in on
        the confirmation screen.
      </Text>
      <Label>Recent scans</Label>
      {scans === null ? <Loading /> : scans.length === 0 ? <Empty text="No receipts scanned yet." /> :
        scans.map(s => (
          <LedgerRow key={s.id}
            left={s.vendorName ?? s.originalFileName}
            sub={s.status === 2 ? 'Confirmed' : s.status === 1 ? 'Awaiting confirmation' : 'Uploaded'}
            amount={s.grossAmount != null ? gbp(s.grossAmount) : undefined}
            onPress={s.status !== 2 ? () => beginReview(s) : undefined} />
        ))}
    </Screen>
  );
}
