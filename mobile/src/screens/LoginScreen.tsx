import React, { useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { useAuth } from '../auth';
import { errorMessage } from '../api';
import { Button, Input, Label, Screen } from '../components/ui';
import { colors, spacing, type } from '../theme';

export default function LoginScreen() {
  const { signIn, register } = useAuth();
  const [mode, setMode] = useState<'login' | 'register'>('login');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [name, setName] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async () => {
    setBusy(true);
    try {
      if (mode === 'login') await signIn(email.trim(), password);
      else await register(email.trim(), password, name.trim());
    } catch (e) {
      Alert.alert('Sign in failed', errorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Screen>
      <View style={{ marginTop: spacing.xl * 2, marginBottom: spacing.l }}>
        <Text style={[type.display, { fontSize: 32 }]}>KestrelBooks</Text>
        <Text style={[type.body, { marginTop: spacing.xs, color: colors.muted }]}>
          Multi-client bookkeeping, double entry done for you.
        </Text>
      </View>
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
      <Input value={password} onChangeText={setPassword} secureTextEntry placeholder="At least 8 characters" />
      <Button title={busy ? '…' : mode === 'login' ? 'Sign in' : 'Create account'} onPress={submit} disabled={busy} />
      <Button kind="ghost"
        title={mode === 'login' ? 'New here? Create an account' : 'Have an account? Sign in'}
        onPress={() => setMode(mode === 'login' ? 'register' : 'login')} />
    </Screen>
  );
}
