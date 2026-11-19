import { APIRequestContext, expect, request, test } from '@playwright/test';

// Flujo principal de la tienda en el navegador: iniciar sesión, abrir caja, vender y cobrar en efectivo, y cerrar
// la caja con el arqueo cuadrado. El CI deja lista la instalación (negocio, primer Administrador y caja) con
// scripts/setup/Initialize-ControlPlus.ps1, como en una tienda nueva; aquí solo se crea el producto por la API y todo
// lo demás se hace en la pantalla como lo haría el usuario.

const baseURL = process.env.E2E_BASE_URL ?? 'http://127.0.0.1:5000';
const adminUser = 'admin.e2e';
const adminPassword = process.env.CONTROLPLUS_E2E_ADMIN_PASSWORD ?? '';
const productName = 'Cuaderno de pruebas E2E';

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
    { data: { name: 'Papelería E2E', description: null, abbreviation: 'PEE' } }), 201);
  const units = await json<{ id: string; code: string }[]>(await api.get('/api/measurement-units'), 200);
  const product = await json<{ id: string }>(await api.post('/api/products', {
    headers: { 'Idempotency-Key': 'e2e-initial-product' },
    data: {
      categoryId: category.id, measurementUnitId: units.find(unit => unit.code === 'UNIDAD')!.id, supplierId: null,
      internalCode: null, barcode: null, barcodeFormat: null, generateBarcode: true, name: productName, description: null,
      retailPrice: 5000, wholesalePrice: null, taxPercentage: null, minimumStock: 0, initialQuantity: 10, initialUnitCost: 3000,
    },
  }), 201);
  productId = product.id;
  const cash = await json<{ isConfigured: boolean; hasOpenShift: boolean }>(await api.get('/api/cash/state'), 200);
  expect(cash, 'El script de arranque debía dejar la caja configurada y sin turno').toMatchObject({ isConfigured: true, hasOpenShift: false });
  await api.dispose();
});

test('iniciar sesión, abrir caja, vender en efectivo y cerrar la caja cuadrada', async ({ page }) => {
  await test.step('iniciar sesión', async () => {
    await page.goto('/ingresar');
    await page.getByLabel('Usuario').fill(adminUser);
    await page.getByLabel('Contraseña').fill(adminPassword);
    await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    await expect(page).toHaveURL(/\/operacion\//);
  });

  await test.step('abrir caja con fondo de $100.000', async () => {
    await page.goto('/operacion/caja');
    await expect(page.getByRole('heading', { name: 'Apertura de caja' })).toBeVisible();
    await page.locator('#initial-amount').fill('100000');
    await expect(page.locator('#initial-amount')).toHaveValue('100.000');
    await page.getByRole('button', { name: 'Abrir caja' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Turno abierto.' })).toBeVisible();
  });

  await test.step('vender 2 unidades y cobrar en efectivo', async () => {
    await page.goto('/operacion/ventas');
    await page.locator('#product-search').fill(productName);
    await page.locator('#product-search').press('Enter');
    const quantity = page.locator(`#quantity-${productId}`);
    await expect(quantity).toHaveValue('1');
    await quantity.fill('2');
    await quantity.press('Tab');
    await page.getByRole('button', { name: /^Cobrar/ }).click();
    await page.locator('#cash-received').fill('20000');
    await expect(page.locator('#cash-received')).toHaveValue('20.000');
    await page.getByRole('button', { name: /^Confirmar venta/ }).click();
    await expect(page.getByRole('heading', { name: 'Venta confirmada' })).toBeVisible();
    const summary = page.locator('.success-summary');
    await expect(summary.locator('div', { hasText: 'Total pagado' })).toContainText('10.000');
    await expect(summary.locator('div', { hasText: 'Cambio' })).toContainText('10.000');
  });

  await test.step('cerrar la caja con el efectivo esperado', async () => {
    await page.goto('/operacion/caja');
    await page.getByRole('button', { name: 'Cerrar caja' }).click();
    await expect(page.locator('.expected-cash-card')).toContainText('110.000');
    const cashRow = page.locator('.cash-count-row', { hasText: 'Efectivo' });
    const countedCash = cashRow.getByRole('textbox');
    await countedCash.fill('110000');
    await expect(countedCash).toHaveValue('110.000');
    await page.getByRole('button', { name: 'Siguiente' }).click();
    await expect(page.locator('.cash-total-difference')).toContainText('$');
    await expect(page.locator('.cash-total-difference strong')).toHaveText(/^\$\s?0$/);
    await page.getByRole('button', { name: 'Siguiente' }).click();
    await page.getByRole('button', { name: 'Cerrar caja' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Turno cerrado.' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Apertura de caja' })).toBeVisible();
  });

  await test.step('las existencias bajaron en la base', async () => {
    const token = await page.evaluate(() => {
      for (let i = 0; i < sessionStorage.length; i++) {
        const value = sessionStorage.getItem(sessionStorage.key(i)!) ?? '';
        if (value.includes('accessToken')) return (JSON.parse(value) as { accessToken: string }).accessToken;
      }
      return '';
    });
    const api = await request.newContext({ baseURL, extraHTTPHeaders: { Authorization: `Bearer ${token}` } });
    const product = await json<{ availableStock?: number; currentStock?: number }>(await api.get(`/api/products/${productId}`), 200);
    expect(product.availableStock ?? product.currentStock).toBe(8);
    await api.dispose();
  });
});
