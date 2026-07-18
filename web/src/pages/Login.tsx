import { useState, type FormEvent } from 'react';
import { useAuth } from '../auth';
import { errorMessage } from '../api';

export default function Login() {
  const { signIn, verifyMfa, requestEmailCode } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [mfaToken, setMfaToken] = useState('');
  const [code, setCode] = useState('');
  const [err, setErr] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setErr(''); setBusy(true);
    try {
      if (mfaToken) await verifyMfa(mfaToken, code, 'totp');
      else {
        const r = await signIn(email, password);
        if (r.mfaRequired) setMfaToken(r.mfaToken!);
      }
    } catch (ex) { setErr(errorMessage(ex)); }
    finally { setBusy(false); }
  };

  return (
    <div className="login">
      <form onSubmit={submit}>
        <h1>KestrelBooks</h1>
        <div className="sub">Practice bookkeeping, double entry done properly.</div>
        {mfaToken ? (
          <>
            <label>Two-factor code</label>
            <input value={code} onChange={e => setCode(e.target.value)} maxLength={6} autoFocus />
            <button className="btn" style={{ marginTop: 16, width: '100%' }} disabled={busy}>
              Verify
            </button>
            <button type="button" className="btn ghost" style={{ marginTop: 8, width: '100%' }}
              onClick={async () => { await requestEmailCode(mfaToken); setErr('Code emailed — enter it above, then Verify.'); }}>
              Email me a code instead
            </button>
          </>
        ) : (
          <>
            <label>Email</label>
            <input value={email} onChange={e => setEmail(e.target.value)} autoFocus />
            <label>Password</label>
            <input type="password" value={password} onChange={e => setPassword(e.target.value)} />
            <button className="btn" style={{ marginTop: 18, width: '100%' }} disabled={busy}>
              Sign in
            </button>
          </>
        )}
        {err && <div className="err">{err}</div>}
      </form>
    </div>
  );
}
