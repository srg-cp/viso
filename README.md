# CapturaTutor

Aplicación de escritorio para Windows 10/11 de 64 bits. Captura un monitor o una región, envía la imagen a un modelo de OpenAI con visión y muestra su respuesta en una ventana en la esquina inferior derecha o izquierda. Interfaz en español.

## Abrir el programa

Abre **dist/CapturaTutor.exe**. Es un ejecutable portable: no requiere instalar Python ni .NET. En el primer inicio, Windows puede tardar unos segundos en extraer los componentes incluidos. El ejecutable no tiene firma digital.

1. En **Configuración**, pega tu **API key de OpenAI**, elige modelo y esquina y pulsa **Guardar configuración**.
2. En **Capturar**, elige el monitor, pantalla completa o región, y la tarea. Puedes agregar indicaciones como «resuelve solo la pregunta 3».
3. Pulsa **Capturar pantalla** o **Alt + Mayús + S** desde otra aplicación. Para una región, arrastra sobre el ejercicio; **Esc** cancela.
4. Revisa la vista previa y pulsa **Enviar al modelo**. La respuesta aparece en el monitor seleccionado, en la esquina configurada. Puedes copiarla, guardarla como texto, mover o redimensionar la ventana.

Al desactivar **Revisar la captura antes de enviarla**, el botón o atajo captura y envía en una sola acción. No hay capturas periódicas ni envíos automáticos en segundo plano. La aplicación y su respuesta se ocultan antes de capturar.

**A la bandeja** permite dejar el programa disponible junto al reloj. Un doble clic en el icono vuelve a abrirlo. El menú de la bandeja permite capturar, ver la respuesta y salir. Minimizar lo oculta en la bandeja; cerrar la ventana principal termina el programa. Si otra aplicación ocupa el atajo, usa el botón o la bandeja. Solo puede abrirse una instancia a la vez.

## Modelos recomendados

Esta primera versión usa **OpenAI**; no acepta claves de Gemini, Claude u otros proveedores.

| Modelo | Uso recomendado |
| --- | --- |
| `gpt-5.4-mini` | Opción inicial por costo y rapidez para ejercicios habituales. |
| `gpt-5.4` | Opción para problemas complejos que requieren mayor razonamiento. |

Ambos admiten imágenes y Responses API. Las recomendaciones son una elección práctica basada en sus capacidades documentadas, no una garantía de exactitud para todos los ejercicios. Se configura razonamiento medio para estos dos modelos. El campo de modelo también permite escribir otro identificador de OpenAI; debe admitir imágenes y Responses API, y estar habilitado en tu cuenta. Para modelos personalizados se usan sus valores predeterminados de razonamiento.

La aplicación permite resolver paso a paso, dar una respuesta breve, completar o explicar un enunciado y revisar una solución. Se indica al modelo que señale datos faltantes, no invente texto ilegible y marque cualquier reconstrucción como propuesta. Las fórmulas se muestran como texto; no hay renderizado de LaTeX. Revisa las respuestas, especialmente signos, gráficos y cifras pequeñas. Para texto pequeño en pantallas grandes, selecciona solo el ejercicio.

Fuentes oficiales consultadas:

- [GPT-5.4](https://developers.openai.com/api/docs/models/gpt-5.4)
- [GPT-5.4 mini](https://developers.openai.com/api/docs/models/gpt-5.4-mini)
- [Imágenes y visión](https://developers.openai.com/api/docs/guides/images-vision)
- [Crear una API key](https://platform.openai.com/api-keys)

## Clave, capturas y consumo

- La clave se conserva en memoria. Al marcar **Recordar clave**, se cifra con Windows DPAPI para el usuario actual y se guarda en `%LOCALAPPDATA%\CapturaTutor\settings.json`. No se guarda en texto plano. Desmarca la opción y guarda para eliminar la copia persistente.
- Las capturas y respuestas permanecen en memoria y se sustituyen al realizar nuevas consultas. No se escriben en disco salvo que guardes una respuesta o la copies al portapapeles.
- Cada consulta envía la captura, la tarea y las indicaciones directamente a `https://api.openai.com/v1/responses`. El acceso usa HTTPS y tu API key. No hay servidor intermediario de esta aplicación.
- Se utiliza `store: false`. Esto desactiva el almacenamiento del objeto de respuesta para recuperarlo mediante la API; no equivale a prometer ausencia de retención del proveedor. Consulta las políticas de tu cuenta OpenAI.
- La API se factura por separado. Necesitas una cuenta API con acceso al modelo y saldo/facturación; una suscripción a ChatGPT por sí sola no configura esta aplicación.
- Se muestran los tokens que devuelve el servicio, incluyendo consumo de razonamiento cuando corresponda. No es un presupuesto ni un precio final. Cada solicitud limita la salida a 12.000 tokens, incluidos los de razonamiento, y admite una imagen PNG de hasta 20 MB.
- **Cancelar** deja de esperar la respuesta; una solicitud ya enviada puede generar un cargo. Las consultas tienen un tiempo máximo de espera de tres minutos y no se reintentan automáticamente.

## Problemas comunes

- **Clave inválida:** copia una clave API válida y comprueba que no esté revocada.
- **Saldo o límite:** comprueba la facturación y límites de tu cuenta API.
- **Modelo no disponible:** selecciona otro modelo con visión habilitado para tu cuenta.
- **Captura negra:** ciertas ventanas con contenido protegido o escritorios seguros de Windows no permiten capturas convencionales.
- **Respuesta incompleta:** selecciona un solo ejercicio, reduce las indicaciones o cambia de modelo.
- **No ves la ventana:** comprueba el monitor seleccionado y abre **Ver respuesta** desde el programa o su icono de bandeja.

## Compilar y verificar

Requiere el SDK de .NET 8 para compilar en Windows. En PowerShell, desde la carpeta del proyecto:

```powershell
.\build.ps1 -Test
```

Genera `dist/CapturaTutor.exe` con el runtime incluido y ejecuta pruebas locales sin clave ni llamadas externas. El código usa WPF, captura nativa de Windows y HttpClient; no requiere paquetes de terceros.

```powershell
dotnet build CapturaTutor/CapturaTutor.csproj -c Release
$p = Start-Process .\dist\CapturaTutor.exe -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
Get-Content .\dist\self-test-result.txt
$p.ExitCode
```

Prueba de escritorio opcional, sobre una sesión interactiva de Windows: `--smoke-test`. Inicia brevemente las ventanas, captura un área local y verifica la posición del panel en ambas esquinas. No envía esa captura. Deja el resultado en `smoke-test-result.txt` y una vista de la interfaz en `smoke-ui.png` junto al ejecutable.

La validación local cubre el formato de la solicitud visual, la autenticación, lectura de respuestas, respuestas incompletas, errores de clave/cuota y cifrado DPAPI. No sustituye una prueba real del servicio con tu API key.
