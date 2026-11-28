"""Windows: python scripts/performance/measure-isolated.py (PostgreSQL 18 y .NET 10)."""
import http.client
import json
import math
import os
from pathlib import Path
import secrets
import socket
import subprocess
import tempfile
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
PG = Path(os.environ["ProgramFiles"]) / "PostgreSQL/18/bin"


def free_port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def measure(api, key, password):
    headers = {"Content-Type": "application/json"}

    def request(path, body=None, extra=None):
        data = json.dumps(body).encode() if body is not None else None
        start = time.perf_counter()
        api.request("POST" if data else "GET", "/api/" + path, data, headers | (extra or {}))
        response = api.getresponse()
        raw = response.read()
        ms = (time.perf_counter() - start) * 1000
        if response.status not in (200, 201):
            raise RuntimeError(f"{path}: HTTP {response.status}")
        return json.loads(raw), ms

    request("auth/setup", dict(establishmentName="Prueba", installationCode="PERF",
            installationSerial="001", terminalCode="POS", terminalName="Prueba",
            userName="admin.perf", displayName="Prueba", password=password),
            {"X-ControlPlus-Master-Key": key})
    auth, _ = request("auth/login", dict(userName="admin.perf", password=password))
    headers["Authorization"] = "Bearer " + auth["accessToken"]
    request("cash/register", dict(code="CAJA", name="Prueba"))
    cat, _ = request("categories", dict(name="Prueba", abbreviation="PRU"))
    units, _ = request("measurement-units")
    unit = next(u["id"] for u in units if u["code"] == "UNIDAD")
    ids = []
    for i in range(100):
        product, _ = request("products", dict(categoryId=cat["id"], measurementUnitId=unit,
            generateBarcode=True, name=f"Articulo PERF{i:03}", retailPrice=5000,
            minimumStock=0, initialQuantity=10, initialUnitCost=3000),
            {"Idempotency-Key": str(uuid.uuid4())})
        ids.append(product["id"])

    samples = {"busqueda": [], "venta": [], "cierre": []}
    for cycle in range(-1, 100):
        i = cycle % 100
        result, search = request(f"products?search=PERF{i:03}")
        if not any(p["id"] == ids[i] for p in result["items"]):
            raise RuntimeError("La búsqueda no devolvió el producto esperado")
        request("cash/shifts", dict(initialAmount=10000, mode="INDIVIDUAL"))
        sale, sale_ms = request("sales", dict(lines=[dict(productId=ids[i], quantity=1,
            priceType="MINORISTA")], payments=[dict(methodCode="EFECTIVO", appliedAmount=5000,
            receivedAmount=5000)]), {"Idempotency-Key": str(uuid.uuid4())})
        rec, _ = request("cash/reconciliation")
        counts = [dict(paymentMethodId=m["paymentMethodId"], countedAmount=m["expectedAmount"])
                  for m in rec["paymentMethods"] if m["affectsCash"]]
        if sale["total"] != 5000 or sum(m["countedAmount"] for m in counts) != 15000:
            raise RuntimeError("El efectivo esperado no coincide con la venta")
        close, close_ms = request(f"cash/shifts/{rec['shiftId']}/close",
            dict(version=rec["shiftVersion"], paymentMethods=counts))
        if close["totalDifference"] != 0:
            raise RuntimeError("Cierre con diferencia")
        if cycle >= 0:
            for values, elapsed in zip(samples.values(), (search, sale_ms, close_ms)):
                values.append(elapsed)
    for name, values in samples.items():
        ordered = sorted(values)
        print(name, "n=100", *(f"p{p}={ordered[math.ceil(len(values)*p/100)-1]:.2f}ms"
            for p in (50, 95, 100)), f"en_objetivo={sum(ms <= 1000 for ms in values)}")
    return all(sum(ms <= 1000 for ms in v) >= 95 for v in samples.values())


def main():
    with tempfile.TemporaryDirectory(prefix="ControlPlus-perf-") as temp:
        directory = Path(temp)
        data = directory / "pg"
        dbport, apiport = free_port(), free_port()
        dbpass = secrets.token_hex(24)
        pwfile = directory / "init.pw"
        pwfile.write_text(dbpass, encoding="ascii")
        env = os.environ | {"PGPASSWORD": dbpass, "PGUSER": "perf", "PGHOST": "127.0.0.1",
                            "PGPORT": str(dbport)}

        def run(*args):
            subprocess.run(args, cwd=ROOT, env=env, check=True)

        run(PG / "initdb", "-D", data, "-U", "perf", "-A", "scram-sha-256",
            "--pwfile", pwfile, "--encoding=UTF8", "--no-locale")
        pwfile.unlink()
        run(PG / "pg_ctl", "-D", data, "-o", f"-h 127.0.0.1 -p {dbport}",
            "-l", directory / "pg.log", "-w", "start")
        try:
            db = "controlplus_e2e_perf"
            run(PG / "createdb", db)
            connection = f"Host=127.0.0.1;Port={dbport};Database={db};Username=perf;Password={dbpass}"
            env.update(CONTROLPLUS_E2E_CONNECTION=connection, ConnectionStrings__ControlPlusDb=connection,
                ASPNETCORE_ENVIRONMENT="Testing", DOTNET_ENVIRONMENT="Testing",
                ASPNETCORE_URLS=f"http://127.0.0.1:{apiport}", HttpsRedirection__Enabled="false",
                Installation__MasterKey=secrets.token_hex(32), Jwt__SigningKey=secrets.token_hex(48),
                Backup__Enabled="false")
            run("dotnet", "run", "e2e/setup/Prepare-E2EDatabase.cs")
            publish = directory / "api"
            run("dotnet", "publish", "ControlPlus.Api", "-c", "Release", "-o", publish)
            with (directory / "api.log").open("wb") as log:
                server = subprocess.Popen(["dotnet", "ControlPlus.Api.dll"], cwd=publish, env=env,
                    stdout=log, stderr=subprocess.STDOUT)
                try:
                    api = http.client.HTTPConnection("127.0.0.1", apiport, timeout=30)
                    for _ in range(100):
                        if server.poll() is not None:
                            raise RuntimeError("La API temporal terminó antes de iniciar")
                        try:
                            api.request("GET", "/api/health/database")
                            response = api.getresponse()
                            response.read()
                            if response.status == 200:
                                break
                        except OSError:
                            api.close()
                        time.sleep(.2)
                    else:
                        raise RuntimeError("La API temporal no está lista")
                    return measure(api, env["Installation__MasterKey"], "Aa1!" + secrets.token_hex(8))
                finally:
                    server.terminate()
                    server.wait()
        finally:
            run(PG / "pg_ctl", "-D", data, "-m", "fast", "-w", "stop")


if __name__ == "__main__":
    raise SystemExit(0 if main() else 1)
