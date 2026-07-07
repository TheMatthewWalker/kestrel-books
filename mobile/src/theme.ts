// KestrelBooks design tokens.
// Identity: the paper ledger, digitised. Ink navy chrome, ruled rows,
// monospaced tabular figures. Debits carry oxide red, credits ledger green —
// the two colours a ledger clerk's pen actually used.
export const colors = {
  ink: '#0F1B2D',        // headers, primary actions
  inkSoft: '#33465F',
  paper: '#FAFBFC',      // screen background
  card: '#FFFFFF',
  rule: '#D9DEE5',       // hairline row rules
  debit: '#A63B22',      // oxide red — money out / Dr
  credit: '#1E7A4E',     // ledger green — money in / Cr
  muted: '#7A8699',
  danger: '#A63B22',
  badgeDraft: '#E8EDF4',
  badgePosted: '#E2F1E8',
};

export const type = {
  display: { fontSize: 26, fontWeight: '700' as const, color: colors.ink, letterSpacing: -0.5 },
  title: { fontSize: 17, fontWeight: '600' as const, color: colors.ink },
  body: { fontSize: 15, color: colors.inkSoft },
  label: { fontSize: 12, fontWeight: '600' as const, color: colors.muted, textTransform: 'uppercase' as const, letterSpacing: 0.8 },
  // The signature: figures are always monospaced and right-aligned, like a ledger column.
  money: { fontSize: 15, fontVariant: ['tabular-nums'] as any, fontFamily: 'Menlo', color: colors.ink },
};

export const spacing = { xs: 4, s: 8, m: 16, l: 24, xl: 32 };

export const gbp = (n: number) =>
  new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP' }).format(n);
