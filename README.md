# Net Monitor

**Versión:** 1.0.0  
**Repositorio:** [github.com/SrDyw/net-monitor](https://github.com/SrDyw/net-monitor)  
**Autor:** [SrDyw](https://github.com/SrDyw)

Monitor de red para Windows: caudal de subida y bajada, latencia, estado del enlace, histórico de marea, modo marca de agua, tráfico por proceso y editor de proxy. Proyecto de código abierto.

## Características

- **Caudal en vivo** sumando los adaptadores que llevan la ruta por defecto, con respaldo para túneles VPN.
- **Velocidad total** (bajada + subida) en el panel AHORA y en la marca de agua.
- **Latencia** por ICMP, con respaldo TCP y HTTP para redes donde el ping está bloqueado (VPN, proxy, cortafuegos).
- **Estado del enlace**: conectado, latencia alta, sin internet o sin red, con adaptador, IP local y puerta de enlace.
- **Gráfico de marea** de 1, 5 o 15 minutos; los cortes quedan marcados como tierra.
- **Totales de sesión** y por proceso (mientras el inspector está activo).
- **Modo marca de agua**: superposición compacta, siempre encima y click-through.
  - `Ctrl+Alt+N`: alterna ventana / marca de agua.
  - `Ctrl+Alt + arrastrar`: mueve la marca de agua; la posición se guarda.
  - `Ctrl+Alt + clic`: vuelve al modo ventana.
  - Minimizar la ventana entra en modo marca de agua y la quita de la barra de tareas.
- **Bandeja del sistema**: mostrar, configuración, procesos, mover marca de agua, esquina, iniciar con Windows y salir.
- **Inspector de procesos** (ETW): tráfico por proceso, destinos, protocolos, registros y totales. Requiere permisos de administrador (UAC).
- **Procesos fijados**: se guardan por nombre, vuelven arriba en su orden y aparecen marcados como “No está abierto” cuando no se están ejecutando.
- **Registro de procesos fijados** junto a la marca de agua: las últimas líneas de tráfico, con contador y número de filas configurable.
- **Editor de proxy**: proxy del sistema de Windows y variables `HTTP_PROXY` / `HTTPS_PROXY` del usuario, con plantillas guardadas.
- **Tema claro/oscuro** siguiendo Windows, interfaz en español y sin telemetría.

## Requisitos

- Windows 10 u 11, **x64**.
- Para usar una build self-contained: no hace falta instalar .NET.
- Para compilar: **.NET 9 SDK** con soporte de WPF (Windows Desktop).

## Uso

1. Ejecuta `NetMonitor.exe`.
2. La ventana principal muestra la marea, la latencia y el resumen de la sesión.
3. La app vive en la bandeja; cerrar la ventana la oculta allí.
4. Atajos:
   - `Ctrl+1` / `Ctrl+2` / `Ctrl+3`: rango del gráfico.
   - `Ctrl+P`: inspector de procesos.
   - `Ctrl+,`: configuración.
   - `Esc`: volver al monitor.
   - `Ctrl+Alt+N`: alternar ventana / marca de agua.
5. El inspector de procesos pedirá permisos de administrador una vez por sesión.
6. El proxy se configura en **Configuración → Proxy → Configurar…**.

## Build

```powershell
dotnet build
dotnet run
```

Publicación self-contained en un solo archivo:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None -p:DebugSymbols=false `
  -o release
```

No se versionan binarios ni carpetas de compilación: `bin/`, `obj/`, `release/`, `dist/`, `share/` y `release-win-x64*/` están ignorados.

## Datos locales

La app guarda todo por usuario en `%AppData%\NetMonitor`:

- `settings.json`: preferencias, esquinas, pines y opacidad.
- `proxy-templates.json`: plantillas de proxy. **Puede contener credenciales de proxy en texto plano; no compartas este archivo.**
- `net-monitor.log`: diagnóstico local.

No se envía nada a ningún servidor.

## Estructura

- `App.xaml.cs`: arranque, bandeja, modos, tema, capturas de diseño y excepciones de UI.
- `MainWindow.xaml` / `WatermarkWindow.xaml`: ventana y superposición.
- `ViewModels/`: estado del monitor, preferencias y procesos.
- `Services/`: muestreo de tráfico, latencia, Wi-Fi, ajustes, proxy, bandeja, inicio, tema y hilo del inspector.
- `Agent/`: agente ETW elevado que envía los flujos por una tubería con nombre.
- `Controls/`: gráfico de marea, trazas, ajustes, procesos y ventanas del registro fijado.
- `Themes/`: placas clara y oscura.
- `Interop/`: P/Invoke de Windows.

## Privacidad

- Sin telemetría, cuentas ni servicios externos.
- El inspector solo lee eventos de red locales y requiere UAC.
- El editor de proxy solo actúa cuando se pulsa **Guardar** y escribe la configuración del usuario actual.

## Licencia

MIT. Consulta [LICENSE](LICENSE).
