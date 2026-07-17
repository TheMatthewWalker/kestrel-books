import React, { useCallback, useEffect, useState } from 'react';
import { Alert, Text, View } from 'react-native';
import * as DocumentPicker from 'expo-document-picker';
import { api, errorMessage } from '../api';
import { Button, Label, LedgerRow } from './ui';
import { colors, spacing } from '../theme';

/**
 * Reusable attachments panel: list, upload (document picker), download hint,
 * delete. entityKind uses the API's AttachedTo enum values.
 */
export default function AttachmentsBlock({ businessId, entityKind, entityId }:
  { businessId: string; entityKind: number; entityId: string }) {
  const [items, setItems] = useState<any[]>([]);

  const load = useCallback(() => {
    api.get(`/businesses/${businessId}/attachments`, { params: { entityKind, entityId } })
      .then(r => setItems(r.data)).catch(() => {});
  }, [businessId, entityKind, entityId]);
  useEffect(load, [load]);

  const upload = async () => {
    const res = await DocumentPicker.getDocumentAsync({ copyToCacheDirectory: true });
    if (res.canceled || !res.assets?.length) return;
    const a = res.assets[0];
    try {
      const form = new FormData();
      form.append('file', { uri: a.uri, name: a.name, type: a.mimeType ?? 'application/pdf' } as any);
      form.append('entityKind', String(entityKind));
      form.append('entityId', entityId);
      await api.post(`/businesses/${businessId}/attachments`, form,
        { headers: { 'Content-Type': 'multipart/form-data' } });
      load();
    } catch (e) { Alert.alert('Upload failed', errorMessage(e)); }
  };

  const remove = (id: string, name: string) => Alert.alert(
    'Delete attachment?', name,
    [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Delete', style: 'destructive',
        onPress: async () => {
          try { await api.delete(`/businesses/${businessId}/attachments/${id}`); load(); }
          catch (e) { Alert.alert('Error', errorMessage(e)); }
        },
      },
    ]);

  return (
    <View style={{ marginTop: spacing.l }}>
      <Label>Attachments</Label>
      {items.length === 0 && (
        <Text style={{ color: colors.muted, fontSize: 12 }}>
          No files attached. Supplier PDFs, contracts, warranties — pin the evidence to the record.
        </Text>
      )}
      {items.map(a => (
        <LedgerRow key={a.id} left={a.fileName}
          sub={`${(a.sizeBytes / 1024).toFixed(0)} KB · ${new Date(a.uploadedAtUtc).toLocaleDateString()}`}
          amount="delete"
          onPress={() => remove(a.id, a.fileName)} />
      ))}
      <Button kind="ghost" title="+ Attach a file" onPress={upload} />
    </View>
  );
}
