import React, { useState } from 'react';
import { Alert, Text } from 'react-native';
import { api, errorMessage } from '../api';
import { useAuth } from '../auth';
import { Button, Input, Label, Screen } from '../components/ui';
import { colors, spacing, type } from '../theme';

export default function SecurityScreen() {
  const { signOut } = useAuth();
  const [setup, setSetup] = useState<any | null>(null);
  const [code, setCode] = useState('');

  const beginSetup = async () => {
    try { setSetup((await api.post('/auth/mfa/setup')).data); }
    catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const confirm = async () => {
    try {
      await api.post('/auth/mfa/confirm', { code: code.trim() });
      Alert.alert('MFA enabled', 'You will be asked for a code at every sign-in. Email fallback is available if you lose the device.');
      setSetup(null); setCode('');
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const disable = async () => {
    try {
      await api.post('/auth/mfa/disable', { code: code.trim() });
      Alert.alert('MFA disabled');
      setCode('');
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  const revokeAll = async () => {
    try {
      await api.post('/auth/revoke-all');
      Alert.alert('Done', 'All sessions revoked — every device must sign in again.');
      await signOut();
    } catch (e) { Alert.alert('Error', errorMessage(e)); }
  };

  return (
    <Screen>
      <Text style={type.title}>Two-factor authentication</Text>
      <Text style={[type.body, { marginTop: spacing.xs }]}>
        Adds a 6-digit code from an authenticator app (Google Authenticator, Authy,
        1Password…) to every sign-in, with email fallback. Recommended — HMRC expects
        MFA on software that files tax.
      </Text>
      {setup ? (
        <>
          <Label>1 — Add to your authenticator app</Label>
          <Text style={[type.body, { fontSize: 13 }]}>
            Add an account manually with this key (or paste the URI into an app that accepts it):
          </Text>
          <Text selectable style={[type.money, { marginVertical: spacing.s, fontSize: 16 }]}>
            {setup.manualKey}
          </Text>
          <Text selectable style={{ fontSize: 11, color: colors.muted }}>{setup.otpAuthUri}</Text>
          <Label>2 — Enter the current code to confirm</Label>
          <Input value={code} onChangeText={setCode} keyboardType="number-pad" maxLength={6} />
          <Button title="Confirm & enable MFA" onPress={confirm} />
          <Button kind="ghost" title="Cancel" onPress={() => setSetup(null)} />
        </>
      ) : (
        <>
          <Button title="Set up authenticator app" onPress={beginSetup} />
          <Label>Already enabled? Enter a current code to disable</Label>
          <Input value={code} onChangeText={setCode} keyboardType="number-pad" maxLength={6} />
          <Button kind="ghost" title="Disable MFA" onPress={disable} />
        </>
      )}

      <Text style={[type.title, { marginTop: spacing.xl }]}>Sessions</Text>
      <Text style={[type.body, { marginTop: spacing.xs }]}>
        Signs out every device, including this one. Use if a device is lost or a
        password may have leaked.
      </Text>
      <Button kind="danger" title="Sign out everywhere" onPress={revokeAll} />
    </Screen>
  );
}
