import {
  APIRequestContext,
  expect,
  Page,
  request,
  test,
} from "@playwright/test";

// Flujos secundarios y de rechazo que complementan los tres recorridos principales. Cada dato de negocio se crea
// en la base descartable controlplus_e2e_*; el comportamiento que se quiere acreditar se ejecuta en el navegador.
const baseURL = process.env.E2E_BASE_URL ?? "http://127.0.0.1:5000";
const adminUser = "admin.e2e";
const adminPassword = process.env.CONTROLPLUS_E2E_ADMIN_PASSWORD ?? "";
const suffix = Date.now().toString();
const mixedProductName = `Producto secundario E2E ${suffix}`;
const lowProductName = `Stock bajo E2E ${suffix}`;
const emptyProductName = `Agotado E2E ${suffix}`;
const customerName = `Cliente apartado E2E ${suffix}`;

let api: APIRequestContext;
let mixedProductId = "";
let lowProductId = "";
let emptyProductId = "";

async function json<T>(
  response: Awaited<ReturnType<APIRequestContext["get"]>>,
  status: number,
): Promise<T> {
  expect(response.status(), await response.text()).toBe(status);
  return (await response.json()) as T;
}

async function login(page: Page): Promise<void> {
  await page.goto("/ingresar");
  await page.getByLabel("Usuario").fill(adminUser);
  await page.getByLabel("Contraseña").fill(adminPassword);
  await page.getByRole("button", { name: "Iniciar sesión" }).click();
  await expect(page).toHaveURL(/\/operacion\//);
}

test.beforeAll(async () => {
  expect(adminPassword, "Falta CONTROLPLUS_E2E_ADMIN_PASSWORD").not.toBe("");
  const anonymous = await request.newContext({ baseURL });
  const session = await json<{ accessToken: string }>(
    await anonymous.post("/api/auth/login", {
      data: { userName: adminUser, password: adminPassword },
    }),
    200,
  );
  await anonymous.dispose();
  api = await request.newContext({
    baseURL,
    extraHTTPHeaders: { Authorization: `Bearer ${session.accessToken}` },
  });

  const category = await json<{ id: string }>(
    await api.post("/api/categories", {
      data: {
        name: `Secundarios E2E ${suffix}`,
        description: null,
        abbreviation: "SFE",
      },
    }),
    201,
  );
  const units = await json<{ id: string; code: string }[]>(
    await api.get("/api/measurement-units"),
    200,
  );
  const unitId = units.find((unit) => unit.code === "UNIDAD")!.id;
  const createProduct = async (
    key: string,
    name: string,
    quantity: number,
    minimumStock: number,
  ) =>
    json<{ id: string }>(
      await api.post("/api/products", {
        headers: { "Idempotency-Key": `e2e-secondary-${key}` },
        data: {
          categoryId: category.id,
          measurementUnitId: unitId,
          supplierId: null,
          internalCode: null,
          barcode: null,
          barcodeFormat: null,
          generateBarcode: true,
          name,
          description: null,
          retailPrice: 4000,
          wholesalePrice: 3500,
          taxPercentage: null,
          minimumStock,
          initialQuantity: quantity,
          initialUnitCost: quantity > 0 ? 2000 : null,
        },
      }),
      201,
    );
  mixedProductId = (await createProduct("mixed", mixedProductName, 20, 2)).id;
  lowProductId = (await createProduct("low", lowProductName, 2, 5)).id;
  emptyProductId = (await createProduct("empty", emptyProductName, 0, 5)).id;
  await json(
    await api.post("/api/customers", {
      data: {
        documentType: "CC",
        documentNumber: `E2E${suffix.slice(-8)}`,
        fullName: customerName,
        phone: "3005550101",
        address: "Calle E2E 10",
        email: null,
        observations: null,
        creditLimit: null,
      },
    }),
    201,
  );
});

test.afterAll(async () => {
  await api?.dispose();
});

test("una clave incorrecta se rechaza y luego permite iniciar sesión con la clave vigente", async ({
  page,
}) => {
  await page.goto("/ingresar");
  await page.getByLabel("Usuario").fill(adminUser);
  await page.getByLabel("Contraseña").fill(`${adminPassword}-incorrecta`);
  await page.getByRole("button", { name: "Iniciar sesión" }).click();
  await expect(page.getByRole("alert")).toContainText(/incorrect|credencial/i);
  await page.getByLabel("Contraseña").fill(adminPassword);
  await page.getByRole("button", { name: "Iniciar sesión" }).click();
  await expect(page).toHaveURL(/\/operacion\//);
});

test("Stock bajo no mezcla productos agotados y Agotados los muestra por separado", async ({
  page,
}) => {
  await login(page);
  await page.goto("/operacion/inventario");
  await page.locator("#stock-status").selectOption("STOCK_BAJO");
  await expect(
    page.locator("tbody tr", { hasText: lowProductName }),
  ).toBeVisible();
  await expect(
    page.locator("tbody tr", { hasText: emptyProductName }),
  ).toHaveCount(0);
  await page.locator("#stock-status").selectOption("AGOTADO");
  await expect(
    page.locator("tbody tr", { hasText: emptyProductName }),
  ).toBeVisible();
  await expect(
    page.locator("tbody tr", { hasText: lowProductName }),
  ).toHaveCount(0);
});

test("un producto agotado se informa y no entra al carrito", async ({
  page,
}) => {
  await login(page);
  await page.goto("/operacion/ventas");
  await page.locator("#product-search").fill(emptyProductName);
  await page.locator("#product-search").press("Enter");
  await expect(page.getByRole("alert")).toContainText(/agotad/i);
  await expect(page.locator(`#quantity-${emptyProductId}`)).toHaveCount(0);
});

test("una venta puede ponerse en espera, retomarse y continuar con el mismo producto", async ({
  page,
}) => {
  await login(page);
  await page.goto("/operacion/ventas");
  await page.locator("#product-search").fill(mixedProductName);
  await page.locator("#product-search").press("Enter");
  await expect(page.locator(`#quantity-${mixedProductId}`)).toHaveValue("1");
  await page.getByRole("button", { name: /Poner en espera/ }).click();
  await expect(
    page.getByRole("button", { name: /En espera \(1\)/ }),
  ).toBeVisible();
  await page.getByRole("button", { name: /En espera \(1\)/ }).click();
  const dialog = page.getByRole("dialog", { name: "Ventas en espera" });
  await dialog.getByRole("button", { name: "Retomar" }).click();
  await expect(page.locator(`#quantity-${mixedProductId}`)).toHaveValue("1");
  await page
    .getByRole("button", { name: `Quitar ${mixedProductName}` })
    .click();
  await expect(page.locator(`#quantity-${mixedProductId}`)).toHaveCount(0);
});

test("una venta combina efectivo y transferencia sin exigir referencia", async ({
  page,
}) => {
  await login(page);
  await page.goto("/operacion/ventas");
  await page.locator("#product-search").fill(mixedProductName);
  await page.locator("#product-search").press("Enter");
  await page.getByRole("button", { name: /^Cobrar/ }).click();
  await page.locator("#cash-received").fill("2000");
  await page.getByRole("button", { name: "+ Pago mixto" }).click();
  await expect(page.locator("#transfer-applied")).toHaveValue("2.000");
  await expect(page.locator("#transfer-reference")).toHaveValue("");
  await page.getByRole("button", { name: /^Confirmar venta/ }).click();
  await expect(
    page.getByRole("heading", { name: "Venta confirmada" }),
  ).toBeVisible();
  await expect(page.locator(".success-summary")).toContainText("4.000");
});

test("un gasto superior al efectivo se rechaza sin cambiar el arqueo", async ({
  page,
}) => {
  await login(page);
  const before = await json<{
    paymentMethods: { code: string; expectedAmount: number }[];
  }>(await api.get("/api/cash/reconciliation"), 200);
  const cashBefore = before.paymentMethods.find(
    (method) => method.code === "EFECTIVO",
  )!.expectedAmount;
  await page.goto("/operacion/caja");
  const form = page.locator(".movement-form");
  await form.locator('select[name="kind"]').selectOption("expenses");
  await form
    .locator('input[name="amount"]')
    .fill(String(Math.trunc(cashBefore + 1)));
  await form.locator('input[name="concept"]').fill("Gasto sin efectivo E2E");
  await form.getByRole("button", { name: "Registrar movimiento" }).click();
  await expect(page.getByRole("alert")).toContainText(/efectivo|fondos/i);
  const after = await json<{
    paymentMethods: { code: string; expectedAmount: number }[];
  }>(await api.get("/api/cash/reconciliation"), 200);
  expect(
    after.paymentMethods.find((method) => method.code === "EFECTIVO")!
      .expectedAmount,
  ).toBe(cashBefore);
});

test("una compra sin pedido crea el proveedor en la misma ventana y guarda el borrador", async ({
  page,
}) => {
  await login(page);
  await page.goto("/operacion/compras");
  await page.getByRole("button", { name: "Recibir mercancía" }).click();
  await page
    .getByRole("button", { name: /No, es una compra sin pedido/ })
    .click();
  await expect(
    page.getByRole("heading", { name: "Compra sin pedido" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "+ Proveedor nuevo" }).click();
  const supplierName = `Proveedor E2E ${suffix}`;
  await page.locator("#new-supplier-name").fill(supplierName);
  await page.getByRole("button", { name: "Crear proveedor" }).click();
  await expect(page.locator("#purchase-supplier")).toContainText(supplierName);
  await page.locator("#purchase-invoice").fill(`FAC-${suffix}`);
  await page
    .locator("#purchase-date")
    .fill(new Date().toISOString().slice(0, 10));
  await page.getByLabel("Producto de la línea 1").selectOption(mixedProductId);
  await page.getByLabel("Cantidad de la línea 1").fill("3");
  await page.getByLabel("Costo unitario de la línea 1").fill("2100");
  await page.getByRole("button", { name: "Guardar borrador" }).click();
  await expect(page.getByText("Borrador guardado.")).toBeVisible();
});

test("un apartado reserva el producto y registra el pago inicial en efectivo", async ({
  page,
}) => {
  await login(page);
  await page.goto("/operacion/apartados");
  await page.getByRole("button", { name: "Nuevo apartado" }).click();
  const dialog = page.getByRole("dialog", { name: "Nuevo apartado" });
  await dialog.locator("#create-customer").fill(customerName);
  await dialog.getByRole("button", { name: "Buscar cliente" }).click();
  await dialog.getByRole("button", { name: new RegExp(customerName) }).click();
  await dialog.locator("#create-product").fill(lowProductName);
  await dialog.getByRole("button", { name: "Buscar producto" }).click();
  await dialog
    .getByRole("button", { name: new RegExp(lowProductName) })
    .click();
  await dialog.locator("#create-amount-0").fill("800");
  await dialog.locator("#create-received-0").fill("1000");
  await dialog.getByRole("button", { name: "Crear apartado" }).click();
  await expect(
    page.locator("tbody tr", { hasText: customerName }),
  ).toBeVisible();
  const product = await json<{
    availableStock?: number;
    reservedStock?: number;
  }>(await api.get(`/api/products/${lowProductId}`), 200);
  expect(product.availableStock).toBe(1);
});
