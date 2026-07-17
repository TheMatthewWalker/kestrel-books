import React, { useCallback, useState } from 'react';
import { Alert, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { api, errorMessage } from '../api';
import { Badge, Button, Empty, LedgerRow, Loading, Screen } from '../components/ui';
import { colors, gbp } from '../theme';

export default function InvoicesScreen({ route, navigation }: any) {
  const { businessId, kind } = route.params; // 'sales' | 'purchase'
  const [items, setItems] = useState<any[] | null>(null);

  useFocusEffect(useCallback(() => {
    navigation.setOptions({ title: kind === 'sales' ? 'Sales Invoices' : 'Purchase Invoices' });
    api.get(`/businesses/${businessId}/${kind}-invoices`)
      .then(r => setItems(r.data)).catch(e => Alert.alert('Error', errorMessage(e)));
  }, [businessId, kind]));

  return (
    <Screen>
      {items === null ? <Loading /> : items.length === 0 ? (
        <Empty text={`No ${kind} invoices yet.`} />
      ) : items.map(i => (
        <View key={i.id}>
          <View style={{ flexDirection: 'row', alignItems: 'center' }}>
            <View style={{ flex: 1 }}>
              <LedgerRow left={`${i.number} — ${i.contact}`} sub={`${i.date}  ·  due ${i.dueDate}`}
                amount={gbp(i.grossTotal)}
                amountColor={kind === 'sales' ? colors.credit : colors.debit}
                onPress={() => navigation.navigate('InvoiceForm', { businessId, kind, invoiceId: i.id })} />
            </View>
            <View style={{ marginLeft: 6 }}><Badge status={i.status} /></View>
          </View>
          {kind === 'sales' && i.status === 'Posted' && (
            <Button kind="ghost" title="Email PDF to customer"
              onPress={async () => {
                try {
                  const r = await api.post(`/businesses/${businessId}/sales-invoices/${i.id}/email`, {});
                  Alert.alert('Sent', `Invoice emailed to ${r.data.sentTo}.`);
                } catch (e) { Alert.alert('Error', errorMessage(e)); }
              }} />
          )}
        </View>
      ))}
      <Button title={`New ${kind} invoice`}
        onPress={() => navigation.navigate('InvoiceForm', { businessId, kind })} />
    </Screen>
  );
}
