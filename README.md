# Padel Match

Aplicacion full-stack para jugadores de padel construida con ASP.NET Core, Angular y SQL Server.

## Stack

- Backend: ASP.NET Core 10, Identity, JWT, Entity Framework Core y SQL Server.
- Frontend: Angular 21 standalone components.
- Pagos: integracion preparada con Mercado Pago Checkout Preferences.
- Autenticacion: email/password, confirmacion por email y Google ID token.

## Levantar el backend

```powershell
dotnet restore .\server\Padel.slnx
dotnet tool restore
dotnet tool run dotnet-ef database update --project .\server\src\Padel.Api\Padel.Api.csproj --startup-project .\server\src\Padel.Api\Padel.Api.csproj
dotnet run --project .\server\src\Padel.Api\Padel.Api.csproj
```

La configuracion local usa SQL Server LocalDB:

```json
"DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=PadelDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
```

Al iniciar en desarrollo se crean roles, un administrador y un club demo:

- Admin: `admin@padel.local`
- Password: `Admin123!`

## Levantar el frontend

```powershell
cd .\client
npm install
npm run start
```

La API esperada por el cliente esta en `https://localhost:7128/api`. Si el puerto cambia, actualiza `client/src/app/api.service.ts`.

## Configuracion externa

Completa estos valores en `server/src/Padel.Api/appsettings.Development.json` o variables de entorno:

- `Jwt:Key`: clave privada para firmar tokens.
- `Google:ClientId`: client ID de Google Identity Services para validar el token en backend.
- `client/src/app/google-auth.config.ts`: el mismo client ID para renderizar el boton oficial de Google en Angular.
- `MercadoPago:AccessToken`: access token de Mercado Pago.
- `MercadoPago:SandboxPayerEmail`: email de un comprador de prueba de Mercado Pago para reservas automaticas en Sandbox.
- `MercadoPago:SuccessUrl`, `FailureUrl`, `PendingUrl`: URLs de retorno del checkout.

## Flujos implementados

- Registro local con token de confirmacion de email emitido por logs.
- Recuperacion de contraseña con token emitido por logs en desarrollo.
- Login con email/password y login con Google ID token.
- Perfil de jugador con foto, datos personales, categoria, nivel, seguidores y seguidos.
- Ranking lineal de `Octava Bajo` a `Primera Alto`; un jugador solo puede unirse directo si esta exactamente un rango arriba o abajo del creador.
- Registro de cancha/club, carga de ubicacion, horarios, cantidad de canchas y precio.
- Aprobacion/rechazo de canchas por administrador.
- Busqueda de disponibilidad por horario primero o por cancha primero.
- Creacion de turnos con bloqueo de cancha.
- Busqueda de turnos compatibles y busqueda completa.
- Union directa, solicitud fuera de rango y aceptacion/rechazo por el creador.
- Notificaciones por turno creado, solicitud, turno completo, cancelacion, aprobacion de cancha y pagos.
- Preferencias de pago Mercado Pago y endpoint webhook.
- Worker que cancela turnos incompletos dos horas antes de empezar y libera la cancha.

## Pruebas

```powershell
dotnet test .\server\Padel.slnx
cd .\client
npm run build
```

## Despliegue en Render

El repo incluye `render.yaml` para crear:

- `padel-api`: backend ASP.NET en Docker.
- `padel-client`: sitio estatico Angular.
- `padel-db`: PostgreSQL administrado por Render.

Antes del primer deploy, configura estas variables en Render:

- `Seed__AdminEmail`: email del administrador inicial.
- `Seed__AdminPassword`: password del administrador inicial.
- `Jwt__Key`: Render puede generarla automaticamente desde el blueprint.

Si cambias los nombres de servicios, actualiza:

- `Cors__AllowedOrigins__0` en `padel-api` con la URL final del frontend.
- `API_BASE_URL` en `padel-client` con la URL final del backend + `/api`.

En Mercado Pago Developers registra como Redirect URL OAuth la URL publica del backend:

```text
https://padel-api.onrender.com/api/account/club-owner/mercadopago/callback
```

Luego entra como administrador en la app desplegada y carga en Pagos:

- `Application ID / Client ID`.
- `Client secret`.
- `Redirect URL OAuth`: la misma URL publica registrada en Mercado Pago.
- URLs de exito/error/pendiente apuntando al frontend desplegado.
- URL webhook apuntando al backend desplegado.
