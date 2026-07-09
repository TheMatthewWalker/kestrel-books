import React, { useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { useAuth } from '../auth';
import { api, errorMessage } from '../api';
import { Button, Input, Label, Screen } from '../components/ui';
import { colors, spacing, type } from '../theme';

type Mode = 'login' | 'register' | 'mfa' | 'forgot' | 'reset';

export default function LoginScreen() {
  const { signIn, verifyMfa, requestEmailCode, register } = useAuth();
  const [mode, setMode] = useState<Mode>('login');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [name, setName] = useState('');
  const [code, setCode] = useState('');
  const [mfaToken, setMfaToken] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [busy, setBusy] = useState(false);

  const run = async (fn: () => Promise<void>) => {
    setBusy(true);
    try { await fn(); }
    catch (e) { Alert.alert('Error', errorMessage(e)); }
    finally { setBusy(false); }
  };

  const submitLogin = () => run(async () => {
    const r = await signIn(email.trim(), password);
    if (r.mfaRequired) { setMfaToken(r.mfaToken!); setCode(''); setMode('mfa'); }
  });

  const submitMfa = (method: 'totp' | 'email') => run(() => verifyMfa(mfaToken, code.trim(), method));

  const submitForgot = () => run(async () => {
    await api.post('/auth/forgot-password', { email: email.trim() });
    Alert.alert('Check your email', 'If that email is registered, a 6-digit code is on its way.');
    setMode('reset');
  });

  const submitReset = () => run(async () => {
    await api.post('/auth/reset-password', { email: email.trim(), code: code.trim(), newPassword });
    Alert.alert('Password changed', 'Sign in with your new password.');
    setPassword(''); setMode('login');
  });

  return (
    <Screen>
      <View style={{ marginTop: spacing.xl * 2, marginBottom: spacing.l }}>
        <Text style={[type.display, { fontSize: 32 }]}>KestrelBooks</Text>
        <Text style={[type.body, { marginTop: spacing.xs, color: colors.muted }]}>
          Multi-client bookkeeping, double entry done for you.
        </Text>
      </View>

      {mode === 'mfa' ? (
        <>
          <Label>Two-factor code</Label>
          <Input value={code} onChangeText={setCode} keyboardType="number-pad"
            placeholder="6-digit code" autoFocus maxLength={6} />
          <Button title={busy ? '…' : 'Verify (authenticator app)'} onPress={() => submitMfa('totp')} disabled={busy} />
          <Button kind="ghost" title="Email me a code instead"
            onPress={() => run(async () => {
              await requestEmailCode(mfaToken);
              Alert.alert('Sent', 'Enter the code from your email, then tap below.');
            })} />
          <Button kind="ghost" title="Verify emailed code" onPress={() => submitMfa('email')} disabled={busy} />
          <Button kind="ghost" title="Back" onPress={() => setMode('login')} />
        </>
      ) : mode === 'forgot' ? (
        <>
          <Label>Email</Label>
          <Input value={email} onChangeText={setEmail} autoCapitalize="none" keyboardType="email-address" />
          <Button title={busy ? '…' : 'Send reset code'} onPress={submitForgot} disabled={busy} />
          <Button kind="ghost" title="Back to sign in" onPress={() => setMode('login')} />
        </>
      ) : mode === 'reset' ? (
        <>
          <Label>6-digit code from your email</Label>
          <Input value={code} onChangeText={setCode} keyboardType="number-pad" maxLength={6} />
          <Label>New password (10+ characters)</Label>
          <Input value={newPassword} onChangeText={setNewPassword} secureTextEntry />
          <Button title={busy ? '…' : 'Set new password'} onPress={submitReset} disabled={busy} />
          <Button kind="ghost" title="Back to sign in" onPress={() => setMode('login')} />
        </>
      ) : (
        <>
          {mode === 'register' && (
            <>
              <Label>Your name</Label>
              <Input value={name} onChangeText={setName} placeholder="Jane Smith" />
            </>
          )}
          <Label>Email</Label>
          <Input value={email} onChangeText={setEmail} autoCapitalize="none"
            keyboardType="email-address" placeholder="you@practice.co.uk" />
          <Label>Password</Label>
          <Input value={password} onChangeText={setPassword} secureTextEntry placeholder="At least 10 characters" />
          <Button title={busy ? '…' : mode === 'login' ? 'Sign in' : 'Create account'}
            onPress={mode === 'login' ? submitLogin
              : () => run(() => register(email.trim(), password, name.trim()))}
            disabled={busy} />
          <Button kind="ghost"
            title={mode === 'login' ? 'New here? Create an account' : 'Have an account? Sign in'}
            onPress={() => setMode(mode === 'login' ? 'register' : 'login')} />
          {mode === 'login' && (
            <Button kind="ghost" title="Forgot password?" onPress={() => { setCode(''); setMode('forgot'); }} />
          )}
        </>
      )}
    </Screen>
  );
}
