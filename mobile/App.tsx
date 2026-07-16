import React from 'react';
import { NavigationContainer, DefaultTheme } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import { StatusBar } from 'expo-status-bar';
import { AuthProvider, useAuth } from './src/auth';
import { colors } from './src/theme';
import LoginScreen from './src/screens/LoginScreen';
import BusinessesScreen from './src/screens/BusinessesScreen';
import DashboardScreen from './src/screens/DashboardScreen';
import AccountsScreen from './src/screens/AccountsScreen';
import ContactsScreen from './src/screens/ContactsScreen';
import ItemsScreen from './src/screens/ItemsScreen';
import InvoicesScreen from './src/screens/InvoicesScreen';
import InvoiceFormScreen from './src/screens/InvoiceFormScreen';
import MoneyScreen from './src/screens/MoneyScreen';
import JournalsScreen from './src/screens/JournalsScreen';
import AssetsScreen from './src/screens/AssetsScreen';
import ReportsScreen from './src/screens/ReportsScreen';
import ReceiptScanScreen from './src/screens/ReceiptScanScreen';
import ReconciliationScreen from './src/screens/ReconciliationScreen';
import InventoryScreen from './src/screens/InventoryScreen';
import ProductionScreen from './src/screens/ProductionScreen';
import MtdScreen from './src/screens/MtdScreen';
import SecurityScreen from './src/screens/SecurityScreen';
import OpeningScreen from './src/screens/OpeningScreen';
import CreditNotesScreen from './src/screens/CreditNotesScreen';

const Stack = createNativeStackNavigator();

const theme = {
  ...DefaultTheme,
  colors: { ...DefaultTheme.colors, background: colors.paper, primary: colors.ink },
};

function Root() {
  const { ready, signedIn } = useAuth();
  if (!ready) return null;
  return (
    <Stack.Navigator
      screenOptions={{
        headerStyle: { backgroundColor: colors.ink },
        headerTintColor: '#fff',
        headerTitleStyle: { fontWeight: '600' },
      }}
    >
      {!signedIn ? (
        <Stack.Screen name="Login" component={LoginScreen} options={{ headerShown: false }} />
      ) : (
        <>
          <Stack.Screen name="Businesses" component={BusinessesScreen} options={{ title: 'Clients' }} />
          <Stack.Screen name="Dashboard" component={DashboardScreen} />
          <Stack.Screen name="Accounts" component={AccountsScreen} options={{ title: 'Chart of Accounts' }} />
          <Stack.Screen name="Contacts" component={ContactsScreen} options={{ title: 'Customers & Vendors' }} />
          <Stack.Screen name="Items" component={ItemsScreen} options={{ title: 'Products & Services' }} />
          <Stack.Screen name="Invoices" component={InvoicesScreen} />
          <Stack.Screen name="InvoiceForm" component={InvoiceFormScreen} options={{ title: 'Invoice' }} />
          <Stack.Screen name="Money" component={MoneyScreen} options={{ title: 'Money In / Out' }} />
          <Stack.Screen name="Journals" component={JournalsScreen} />
          <Stack.Screen name="Assets" component={AssetsScreen} options={{ title: 'Fixed Assets' }} />
          <Stack.Screen name="Reports" component={ReportsScreen} />
          <Stack.Screen name="ReceiptScan" component={ReceiptScanScreen} options={{ title: 'Scan Receipt' }} />
          <Stack.Screen name="Reconciliation" component={ReconciliationScreen} options={{ title: 'Bank Reconciliation' }} />
          <Stack.Screen name="Inventory" component={InventoryScreen} options={{ title: 'Inventory' }} />
          <Stack.Screen name="Production" component={ProductionScreen} options={{ title: 'Production' }} />
          <Stack.Screen name="Mtd" component={MtdScreen} options={{ title: 'HMRC — Making Tax Digital' }} />
          <Stack.Screen name="Security" component={SecurityScreen} options={{ title: 'Account Security' }} />
          <Stack.Screen name="Opening" component={OpeningScreen} options={{ title: 'Opening Balances' }} />
          <Stack.Screen name="CreditNotes" component={CreditNotesScreen} options={{ title: 'Credit Notes' }} />
        </>
      )}
    </Stack.Navigator>
  );
}

export default function App() {
  return (
    <AuthProvider>
      <NavigationContainer theme={theme}>
        <StatusBar style="light" />
        <Root />
      </NavigationContainer>
    </AuthProvider>
  );
}
