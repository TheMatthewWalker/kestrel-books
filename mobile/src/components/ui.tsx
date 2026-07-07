import React from 'react';
import {
  ActivityIndicator, Pressable, ScrollView, StyleSheet, Text, TextInput, View,
} from 'react-native';
import { colors, spacing, type, gbp } from '../theme';

export function Screen({ children, scroll = true }: { children: React.ReactNode; scroll?: boolean }) {
  const inner = <View style={styles.screenInner}>{children}</View>;
  return scroll ? (
    <ScrollView style={styles.screen} keyboardShouldPersistTaps="handled">{inner}</ScrollView>
  ) : (
    <View style={styles.screen}>{inner}</View>
  );
}

export const Label = ({ children }: { children: React.ReactNode }) => (
  <Text style={[type.label, { marginBottom: spacing.xs, marginTop: spacing.m }]}>{children}</Text>
);

export function Input(props: React.ComponentProps<typeof TextInput>) {
  return <TextInput placeholderTextColor={colors.muted} {...props} style={[styles.input, props.style]} />;
}

export function Button({ title, onPress, kind = 'primary', disabled }: {
  title: string; onPress: () => void; kind?: 'primary' | 'ghost' | 'danger'; disabled?: boolean;
}) {
  return (
    <Pressable
      onPress={onPress}
      disabled={disabled}
      style={({ pressed }) => [
        styles.button,
        kind === 'ghost' && styles.buttonGhost,
        kind === 'danger' && { backgroundColor: colors.danger },
        (pressed || disabled) && { opacity: 0.6 },
      ]}
    >
      <Text style={[styles.buttonText, kind === 'ghost' && { color: colors.ink }]}>{title}</Text>
    </Pressable>
  );
}

/** A ruled ledger row: description on the left, monospaced figure on the right. */
export function LedgerRow({ left, sub, amount, amountColor, onPress }: {
  left: string; sub?: string; amount?: string; amountColor?: string; onPress?: () => void;
}) {
  return (
    <Pressable onPress={onPress} disabled={!onPress}
      style={({ pressed }) => [styles.row, pressed && { backgroundColor: '#F1F4F8' }]}>
      <View style={{ flex: 1, paddingRight: spacing.s }}>
        <Text style={type.body} numberOfLines={1}>{left}</Text>
        {sub ? <Text style={{ fontSize: 12, color: colors.muted, marginTop: 2 }}>{sub}</Text> : null}
      </View>
      {amount !== undefined && (
        <Text style={[type.money, { color: amountColor ?? colors.ink }]}>{amount}</Text>
      )}
    </Pressable>
  );
}

export const Money = ({ value, color }: { value: number; color?: string }) => (
  <Text style={[type.money, color ? { color } : null]}>{gbp(value)}</Text>
);

export function Badge({ status }: { status: number | string }) {
  const posted = status === 1 || status === 'Posted';
  const reversed = status === 2 || status === 'Reversed' || status === 'Voided';
  const label = posted ? 'Posted' : reversed ? 'Reversed' : 'Draft';
  return (
    <View style={[styles.badge, { backgroundColor: posted ? colors.badgePosted : colors.badgeDraft }]}>
      <Text style={{ fontSize: 11, fontWeight: '600', color: posted ? colors.credit : colors.inkSoft }}>
        {label}
      </Text>
    </View>
  );
}

export const Loading = () => (
  <View style={{ padding: spacing.xl, alignItems: 'center' }}>
    <ActivityIndicator color={colors.ink} />
  </View>
);

export const Empty = ({ text }: { text: string }) => (
  <Text style={{ color: colors.muted, padding: spacing.l, textAlign: 'center' }}>{text}</Text>
);

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.paper },
  screenInner: { padding: spacing.m, paddingBottom: spacing.xl * 2 },
  input: {
    backgroundColor: colors.card, borderWidth: 1, borderColor: colors.rule,
    borderRadius: 8, padding: 12, fontSize: 15, color: colors.ink,
  },
  button: {
    backgroundColor: colors.ink, borderRadius: 8, padding: 14,
    alignItems: 'center', marginTop: spacing.m,
  },
  buttonGhost: { backgroundColor: 'transparent', borderWidth: 1, borderColor: colors.rule },
  buttonText: { color: '#fff', fontWeight: '600', fontSize: 15 },
  row: {
    flexDirection: 'row', alignItems: 'center', paddingVertical: 12,
    borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: colors.rule,
  },
  badge: { borderRadius: 4, paddingHorizontal: 6, paddingVertical: 2, alignSelf: 'flex-start' },
});
