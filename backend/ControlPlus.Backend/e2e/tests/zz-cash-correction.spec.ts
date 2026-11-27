import { expect, request, test } from '@playwright/test';

// Este recorrido también funciona solo: el CI prepara una base controlplus_e2e_* y la instalación,
// y la prueba abre un turno individual si los recorridos anteriores dejaron la caja cerrada.
const baseURL = process.env.E2E_BASE_URL ?? 'http://127.0.0.1:5000';
const adminUser = 'admin.e2e';
const adminPassword = process.env.CONTROLPLUS_E2E_ADMIN_PASSWORD ?? '';
interface BrowserSession {
  accessToken: string;
  expiresAtUtc: string;
  user: unknown;
  passwordChangeRequired?: boolean;
}

test('corregir un ingreso manual conserva el original y ajusta el efectivo esperado', async ({ page }) => {
  expect(adminPassword, 'Falta CONTROLPLUS_E2E_ADMIN_PASSWORD').not.toBe('');
  const anonymous = await request.newContext({ baseURL });
  const login = await anonymous.post('/api/auth/login', { data: { userName: adminUser, password: adminPassword } });
  expect(login.status(), await login.text()).toBe(200);
  const session = (await login.json()) as BrowserSession;
  await anonymous.dispose();
  const api = await request.newContext({ baseURL, extraHTTPHeaders: { Authorization: `Bearer ${session.accessToken}` } });

  try {
    await page.addInitScript((savedSession) => {
      window.sessionStorage.setItem('controlplus.session', JSON.stringify(savedSession));
    }, session);
    await page.goto('/operacion');
    await expect(page).toHaveURL(/\/operacion\//);
    await page.goto('/operacion/caja');
    await expect(page.locator('.opening-form, .movement-form')).toBeVisible();
    if (await page.locator('.opening-form').isVisible()) {
      await page.locator('#initial-amount').fill('100000');
      await page.getByRole('button', { name: 'Abrir caja', exact: true }).click();
      await expect(page.locator('.movement-form')).toBeVisible();
    }

    const beforeResponse = await api.get('/api/cash/reconciliation');
    expect(beforeResponse.status(), await beforeResponse.text()).toBe(200);
    const before = await beforeResponse.json() as { paymentMethods: { code: string; expectedAmount: number }[] };
    const cashBefore = before.paymentMethods.find(method => method.code === 'EFECTIVO')!.expectedAmount;

    const concept = `Ingreso corregible E2E ${Date.now()}`;
    const form = page.locator('.movement-form');
    await form.locator('select[name="kind"]').selectOption('incomes');
    await form.locator('input[name="amount"]').fill('40');
    await form.locator('input[name="concept"]').fill(concept);
    await form.getByRole('button', { name: 'Registrar movimiento' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Movimiento registrado.' })).toBeVisible();

    await page.getByRole('button', { name: 'Corregir un error de caja' }).click();
    const dialog = page.getByRole('dialog', { name: 'Corregir un error de caja' });
    await dialog.getByRole('radio', { name: /Un ingreso, gasto o retiro quedó mal/ }).click();
    const movement = dialog.locator('select[name="movement"]');
    const option = movement.locator('option', { hasText: concept });
    await expect(option).toHaveCount(1);
    const movementId = await option.getAttribute('value');
    expect(movementId).toBeTruthy();
    await movement.selectOption(movementId!);
    await expect(dialog.locator('input[name="amount"]')).toHaveValue('40');
    await dialog.locator('input[name="amount"]').fill('20');
    await dialog.locator('textarea[name="reason"]').fill('El ingreso correcto era de 20');
    await expect(dialog.locator('.cash-fix-summary')).toContainText('Así quedó');
    await expect(dialog.locator('.cash-fix-summary')).toContainText('Así quedará');
    await expect(dialog.getByRole('status')).toContainText('baja $20');
    await dialog.getByRole('button', { name: 'Revisar y confirmar' }).click();
    await expect(dialog).toBeHidden();

    const afterResponse = await api.get('/api/cash/reconciliation');
    expect(afterResponse.status(), await afterResponse.text()).toBe(200);
    const after = await afterResponse.json() as { paymentMethods: { code: string; expectedAmount: number }[] };
    expect(after.paymentMethods.find(method => method.code === 'EFECTIVO')!.expectedAmount).toBe(cashBefore + 20);
    const historyResponse = await api.get('/api/cash/movements?page=1&pageSize=50');
    expect(historyResponse.status(), await historyResponse.text()).toBe(200);
    const history = await historyResponse.json() as { items: { id: string; type: string; category: string;
      amount: number; concept: string | null; correlationId: string }[] };
    const original = history.items.find(item => item.id === movementId);
    expect(original).toMatchObject({ type: 'INGRESO', category: 'INGRESO_MANUAL', amount: 40, concept });
    const reversal = history.items.find(item => item.type === 'REVERSO' && item.category === 'CORRECCION_MANUAL' && item.amount === 40);
    expect(reversal).toBeDefined();
    const replacement = history.items.find(item => item.type === 'INGRESO' && item.amount === 20 && item.concept === concept);
    expect(replacement?.correlationId).toBe(reversal?.correlationId);
  } finally {
    await api.dispose();
  }
});
