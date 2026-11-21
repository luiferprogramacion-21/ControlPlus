import { APIRequestContext, expect, request, test } from '@playwright/test';

// Venta a crédito y abono en el navegador (21 de noviembre de 2026). Corre después de main-flow.spec.ts (orden
// alfabético, un solo worker), que deja la caja cerrada con un fondo anterior de $100.000. Recorre tres cambios de
// usabilidad aprobados ese día:
//   5. abrir caja propone el fondo de la última apertura («Igual que la última vez»);
//   6. el cliente nuevo para un crédito pide nombre, documento, teléfono y dirección;
//   1 y 7. el abono llega con todo el saldo y Nequi se registra sin referencia.

const baseURL = process.env.E2E_BASE_URL ?? 'http://127.0.0.1:5000';
const adminUser = 'admin.e2e';
const adminPassword = process.env.CONTROLPLUS_E2E_ADMIN_PASSWORD ?? '';
const productName = 'Agenda de crédito E2E';
const customerName = 'Cliente Crédito E2E';

let productId = '';

async function json<T>(response: Awaited<ReturnType<APIRequestContext['get']>>, status: number): Promise<T> {
  expect(response.status(), await response.text()).toBe(status);
  return (await response.json()) as T;
}

test.beforeAll(async () => {
  expect(adminPassword, 'Falta CONTROLPLUS_E2E_ADMIN_PASSWORD').not.toBe('');
  const anonymous = await request.newContext({ baseURL });
  const login = await json<{ accessToken: string }>(await anonymous.post('/api/auth/login',
    { data: { userName: adminUser, password: adminPassword } }), 200);
  await anonymous.dispose();

  const api = await request.newContext({ baseURL, extraHTTPHeaders: { Authorization: `Bearer ${login.accessToken}` } });
  const category = await json<{ id: string }>(await api.post('/api/categories',
    { data: { name: 'Agendas E2E', description: null, abbreviation: 'AGE' } }), 201);
  const units = await json<{ id: string; code: string }[]>(await api.get('/api/measurement-units'), 200);
  const product = await json<{ id: string }>(await api.post('/api/products', {
    headers: { 'Idempotency-Key': 'e2e-credit-product' },
    data: {
      categoryId: category.id, measurementUnitId: units.find(unit => unit.code === 'UNIDAD')!.id, supplierId: null,
      internalCode: null, barcode: null, barcodeFormat: null, generateBarcode: true, name: productName, description: null,
      retailPrice: 8000, wholesalePrice: null, taxPercentage: null, minimumStock: 0, initialQuantity: 5, initialUnitCost: 4000,
    },
  }), 201);
  productId = product.id;
  const cash = await json<{ hasOpenShift: boolean }>(await api.get('/api/cash/state'), 200);
  expect(cash, 'main-flow.spec.ts debía dejar la caja cerrada').toMatchObject({ hasOpenShift: false });
  await api.dispose();
});

test('vender a crédito a un cliente nuevo y abonar todo el saldo por Nequi sin referencia', async ({ page }) => {
  await test.step('iniciar sesión', async () => {
    await page.goto('/ingresar');
    await page.getByLabel('Usuario').fill(adminUser);
    await page.getByLabel('Contraseña').fill(adminPassword);
    await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    await expect(page).toHaveURL(/\/operacion\//);
  });

  await test.step('abrir caja con el fondo de la última apertura', async () => {
    await page.goto('/operacion/caja');
    await expect(page.getByRole('heading', { name: 'Apertura de caja' })).toBeVisible();
    await expect(page.locator('#initial-amount')).toHaveValue('100.000');
    await page.locator('#initial-amount').fill('50000');
    await page.getByRole('button', { name: /^Igual que la última vez/ }).click();
    await expect(page.locator('#initial-amount')).toHaveValue('100.000');
    await page.getByRole('button', { name: 'Abrir caja', exact: true }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Turno abierto.' })).toBeVisible();
  });

  await test.step('vender a crédito registrando el cliente desde la venta', async () => {
    await page.goto('/operacion/ventas');
    await page.locator('#product-search').fill(productName);
    await page.locator('#product-search').press('Enter');
    await expect(page.locator(`#quantity-${productId}`)).toHaveValue('1');
    await page.getByRole('button', { name: 'Registrar crédito' }).click();

    await page.getByRole('button', { name: '+ Nuevo cliente' }).click();
    await page.locator('#customer-new-name').fill(customerName);
    await page.locator('#customer-new-doc-type').selectOption('CC');
    await page.getByLabel('Número de documento').fill('E2E900123');
    await page.locator('#customer-new-phone').fill('3001234567');
    await expect(page.locator('#customer-new-address')).toBeVisible();
    await page.locator('#customer-new-address').fill('Calle 10 # 5-20');
    await page.getByRole('button', { name: 'Guardar y seleccionar' }).click();

    await expect(page.locator('#credit-due-date')).not.toHaveValue('');
    await page.getByRole('button', { name: /^Confirmar crédito/ }).click();
    await expect(page.getByRole('heading', { name: 'Crédito registrado' })).toBeVisible();
  });

  await test.step('abonar todo el saldo por Nequi sin referencia', async () => {
    await page.goto('/operacion/creditos');
    const row = page.locator('tbody tr', { hasText: customerName });
    await row.getByRole('button', { name: 'Abonar' }).click();
    await expect(page.getByRole('heading', { name: 'Registrar abono' })).toBeVisible();
    await expect(page.locator('#credit-amount-0')).toHaveValue('8.000');
    await page.locator('#credit-method-0').selectOption('NEQUI');
    await expect(page.locator('#credit-reference-0')).toHaveValue('');
    await page.getByRole('button', { name: 'Registrar abono' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Abono registrado.' })).toBeVisible();
    await expect(row.getByRole('button', { name: 'Abonar' })).toBeDisabled();
  });
});
