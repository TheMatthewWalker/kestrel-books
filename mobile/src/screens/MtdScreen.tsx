import React, { useCallback, useState } from 'react';
import { Alert, Dimensions, Linking, Platform, Text, View } from 'react-native';
import * as SecureStore from 'expo-secure-store';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Button, Empty, Input, Label, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, gbp, spacing, type } from '../theme';

const boxDefs: { key: string; label: string }[] = [
  { key: 'vatDueSales', label: 'Box 1 — VAT due on sales' },
  { key: 'vatDueAcquisitions', label: 'Box 2 — VAT due on NI acquisitions' },
  { key: 'totalVatDue', label: 'Box 3 — total VAT due (1+2)' },
  { key: 'vatReclaimedCurrPeriod', label: 'Box 4 — VAT reclaimed on purchases' },
  { key: 'netVatDue', label: 'Box 5 — net VAT (|3−4|)' },
  { key: 'totalValueSalesExVAT', label: 'Box 6 — sales ex VAT (whole £)' },
  { key: 'totalValuePurchasesExVAT', label: 'Box 7 — purchases ex VAT (whole £)' },
  { key: 'totalValueGoodsSuppliedExVAT', label: 'Box 8 — NI→EU goods' },
  { key: 'totalAcquisitionsExVAT', label: 'Box 9 — EU→NI goods' },
];

function timezone(): string {
  const mins = -new Date().getTimezoneOffset();
  const sign = mins >= 0 ? '+' : '-';
  const abs = Math.abs(mins);
  const h = String(Math.floor(abs / 60)).padStart(2, '0');
  const m = String(abs % 60).padStart(2, '0');
  return `UTC${sign}${h}:${m}`;
}

export default function MtdScreen({ route }: any) {
  const { businessId } = route.params;
  const [status, setStatus] = useState<any | null>(null);
  const [scheme, setScheme] = useState<any | null>(null);
  const [ratePct, setRatePct] = useState('');
  const [vrn, setVrn] = useState('');
  const [nino, setNino] = useState('');
  const [obligations, setObligations] = useState<any[] | null>(null);
  const [submissions, setSubmissions] = useState<any[]>([]);
  const [current, setCurrent] = useState<any | null>(null); // obligation being filed
  const [boxes, setBoxes] = useState<any | null>(null);
  const [finalised, setFinalised] = useState(false);
  const [itsa, setItsa] = useState<any | null>(null);

  const load = useCallback(() => {
    api.get(`/mtd/businesses/${businessId}/status`).then(r => {
      setStatus(r.data);
      setVrn(r.data.vrn ?? '');
      setNino(r.data.nino ?? '');
      if (r.data.connected && !r.data.deviceRegistered) registerDevice();
      if (r.data.connected && r.data.vrn) loadObligations();
    }).catch(e => Alert.alert('Error', errorMessage(e)));
    api.get(`/mtd/businesses/${businessId}/vat/submissions`).then(r => setSubmissions(r.data)).catch(() => {});
    api.get(`/mtd/businesses/${businessId}/vat-scheme`)
      .then(r => { setScheme(r.data); setRatePct(String(r.data.flatRatePercent || '')); })
      .catch(() => {});
  }, [businessId]);
  useFocusEffect(load);

  const registerDevice = async () => {
    let deviceId = await SecureStore.getItemAsync('deviceId');
    if (!deviceId) {
      deviceId = `${Date.now()}-${Math.random().toString(36).slice(2)}`;
      await SecureStore.setItemAsync('deviceId', deviceId);
    }
    const { width, height, scale } = Dimensions.get('screen');
    await api.put(`/mtd/businesses/${businessId}/device`, {
      deviceId,
      os: `${Platform.OS} ${Platform.Version}`,
      timezone: timezone(),
      screenWidth: String(Math.round(width)),
      screenHeight: String(Math.round(height)),
      scaleFactor: String(scale),
    }).catch(() => {});
  };

  const connect = async () => {
    try {
      const r = await api.get(`/mtd/businesses/${businessId}/authorise-url`);
      await Linking.openURL(r.data.url);
      Alert.alert('Authorise in browser', 'Sign in with the Government Gateway account, grant access, then return here and pull to refresh.');
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const saveDetails = async () => {
    try {
      await api.put(`/mtd/businesses/${businessId}/details`, { vrn: vrn || null, nino: nino || null });
      Alert.alert('Saved', 'HMRC identifiers updated.');
      load();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const loadObligations = async () => {
    try {
      const r = await api.get(`/mtd/businesses/${businessId}/vat/obligations`, { params: { status: 'O' } });
      setObligations(r.data.obligations ?? []);
    } catch (e) { setObligations([]); }
  };

  const openObligation = async (ob: any) => {
    setCurrent(ob); setBoxes(null); setFinalised(false);
    try {
      const r = await api.get(`/mtd/businesses/${businessId}/vat/preview`,
        { params: { from: ob.start, to: ob.end } });
      setBoxes(r.data);
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const setBox = (key: string, v: string) => {
    const n = parseFloat(v) || 0;
    setBoxes((b: any) => {
      const next = { ...b, [key]: n };
      next.totalVatDue = Math.round((next.vatDueSales + next.vatDueAcquisitions) * 100) / 100;
      next.netVatDue = Math.abs(Math.round((next.totalVatDue - next.vatReclaimedCurrPeriod) * 100) / 100);
      return next;
    });
  };

  const submit = async () => {
    try {
      const r = await api.post(`/mtd/businesses/${businessId}/vat/submit`, {
        periodKey: current.periodKey, from: current.start, to: current.end, boxes, finalised,
      });
      Alert.alert('Submitted to HMRC', `Receipt (form bundle): ${r.data.formBundleNumber ?? 'received'}.`);
      setCurrent(null);
      load(); loadObligations();
    } catch (e) { Alert.alert('Submission failed', errorMessage(e)); }
  };

  const loadItsa = async () => {
    try {
      const [b, o] = await Promise.all([
        api.get(`/mtd/businesses/${businessId}/itsa/businesses`),
        api.get(`/mtd/businesses/${businessId}/itsa/obligations`),
      ]);
      setItsa({ businesses: b.data, obligations: o.data });
    } catch (e) { Alert.alert('ITSA', errorMessage(e)); }
  };

  if (status === null) return <Screen><Loading /></Screen>;

  if (!status.configured) {
    return (
      <Screen>
        <Text style={type.title}>HMRC credentials not configured</Text>
        <Text style={[type.body, { marginTop: spacing.s }]}>
          Register the app at developer.service.hmrc.gov.uk (free sandbox), subscribe it to the
          VAT (MTD) and Income Tax (Self Assessment) APIs, set the redirect URI to your server's
          /api/mtd/callback, then fill the Hmrc section of appsettings.json and restart the server.
        </Text>
      </Screen>
    );
  }

  // Filing view for one obligation
  if (current) {
    return (
      <Screen>
        <Text style={type.title}>VAT return {current.periodKey}</Text>
        <Text style={{ color: colors.muted, fontSize: 12 }}>{current.start} → {current.end} · due {current.due}</Text>
        {boxes === null ? <Loading /> : (
          <>
            <Text style={{ color: colors.muted, fontSize: 12, marginTop: spacing.s }}>
              Computed from the ledger — check and adjust before submitting. Boxes 3 and 5 recalculate.
            </Text>
            {boxDefs.map(b => (
              <View key={b.key}>
                <Label>{b.label}</Label>
                <Input value={String(boxes[b.key])} keyboardType="decimal-pad"
                  editable={b.key !== 'totalVatDue' && b.key !== 'netVatDue'}
                  onChangeText={v => setBox(b.key, v)} />
              </View>
            ))}
            <Label>Declaration</Label>
            <Text onPress={() => setFinalised(!finalised)}
              style={{
                padding: spacing.s, borderRadius: 8, overflow: 'hidden', fontSize: 13,
                backgroundColor: finalised ? colors.credit : colors.badgeDraft,
                color: finalised ? '#fff' : colors.ink,
              }}>
              {finalised ? '✓ ' : ''}When you submit this VAT information you are making a legal
              declaration that the information is true and complete. A false declaration can
              result in prosecution. (Tap to {finalised ? 'withdraw' : 'agree'}.)
            </Text>
            <Button title="Submit to HMRC" onPress={submit} disabled={!finalised} />
          </>
        )}
        <Button kind="ghost" title="Back" onPress={() => setCurrent(null)} />
      </Screen>
    );
  }

  return (
    <Screen>
      <Text style={type.title}>VAT scheme</Text>
      <Text style={[type.body, { marginTop: spacing.xs, color: colors.muted }]}>
        {scheme?.scheme === 1 ? 'Cash accounting — VAT follows payment dates.'
          : scheme?.scheme === 2 ? `Flat rate ${scheme?.flatRatePercent}% — box 1 is a % of VAT-inclusive turnover received; no input VAT recovery.`
          : 'Standard (accrual) — VAT follows invoice dates.'}
      </Text>
      <View style={{ flexDirection: 'row', gap: spacing.s, marginTop: spacing.s, flexWrap: 'wrap' }}>
        {[[0, 'Standard'], [1, 'Cash'], [2, 'Flat rate']].map(([v, label]) => (
          <Text key={String(v)}
            onPress={async () => {
              try {
                const r = await api.put(`/mtd/businesses/${businessId}/vat-scheme`,
                  { scheme: v, flatRatePercent: v === 2 ? parseFloat(ratePct) || 0 : 0 });
                setScheme(r.data);
              } catch (e) { Alert.alert('Error', errorMessage(e)); }
            }}
            style={{
              paddingVertical: 6, paddingHorizontal: 12, borderRadius: 6, overflow: 'hidden', fontSize: 13,
              backgroundColor: scheme?.scheme === v ? colors.ink : colors.badgeDraft,
              color: scheme?.scheme === v ? '#fff' : colors.ink, fontWeight: '600',
            }}>{label as string}</Text>
        ))}
      </View>
      {scheme?.scheme !== 2 && (
        <View>
          <Label>Flat rate % (set before choosing Flat rate)</Label>
          <Input value={ratePct} onChangeText={setRatePct} keyboardType="decimal-pad" placeholder="e.g. 14.5" />
        </View>
      )}

      {!status.connected ? (
        <>
          <Text style={type.body}>Connect this client's Government Gateway account to HMRC.</Text>
          <Button title="Connect to HMRC" onPress={connect} />
        </>
      ) : (
        <>
          <Text style={[type.body, { color: colors.credit }]}>
            ✓ Connected to HMRC ({status.scope})
          </Text>
          <Label>VAT registration number (VRN)</Label>
          <Input value={vrn} onChangeText={setVrn} keyboardType="number-pad" placeholder="123456789" />
          <Label>National Insurance number (ITSA)</Label>
          <Input value={nino} onChangeText={setNino} autoCapitalize="characters" placeholder="QQ123456C" />
          <Button kind="ghost" title="Save identifiers" onPress={saveDetails} />

          <Label>Open VAT obligations</Label>
          {obligations === null ? <Loading /> : obligations.length === 0 ? (
            <Empty text="No open obligations returned (check the VRN, or everything is filed)." />
          ) : obligations.map((ob: any) => (
            <LedgerRow key={ob.periodKey} left={`Period ${ob.periodKey}`}
              sub={`${ob.start} → ${ob.end} · due ${ob.due}`}
              onPress={() => openObligation(ob)} />
          ))}

          {submissions.length > 0 && (
            <>
              <Label>Submitted returns</Label>
              {submissions.map((s: any) => (
                <LedgerRow key={s.id} left={`Period ${s.periodKey}`}
                  sub={`Submitted ${String(s.submittedAtUtc).slice(0, 10)} · bundle ${s.formBundleNumber ?? '—'}`} />
              ))}
            </>
          )}

          <Label>Income Tax (ITSA)</Label>
          <Button kind="ghost" title="Fetch ITSA businesses & obligations" onPress={loadItsa} />
          {itsa && (
            <Text style={{ fontSize: 12, color: colors.inkSoft, marginTop: spacing.s }}>
              {JSON.stringify(itsa.businesses).slice(0, 400)}
              {'\n\n'}Quarterly updates submit from the P&L via POST /mtd/businesses/:id/itsa/quarterly —
              see docs/ARCHITECTURE.md for the workflow.
            </Text>
          )}
        </>
      )}
    </Screen>
  );
}
