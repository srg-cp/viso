# CapturaTutor

Programa portable para Windows 10/11 de 64 bits. Captura la pantalla o una región y muestra una respuesta breve en la esquina inferior derecha o izquierda.

## Uso

Abre **dist/interfaz-limpia/CapturaTutor.exe**. No requiere instalar Python ni .NET. Si la versión anterior está abierta, ciérrala desde su icono junto al reloj antes de abrir la nueva.

1. En **Configuración**, selecciona **OpenAI** o **Gemini**, pega la clave correspondiente, elige un modelo y pulsa **Guardar**. No necesitas ambas claves.
2. En **Configuración → General → Tipo de enunciado**, elige **Elegir una alternativa** para seleccionar la alternativa correcta, o **Completar el enunciado** para completar lo que falta.
3. Pulsa **Ocultar** y utiliza tu atajo (por defecto **Alt + Mayús + S**): captura y envía automáticamente.

La ventana de Configuración usa una barra lateral de estilo macOS para General, Captura y atajos, Modelo de IA y Respuesta. Incluye una barra de título propia con tres botones: rojo cierra la aplicación, amarillo minimiza a la bandeja y verde maximiza o restaura. Se puede arrastrar desde la barra superior, maximizar con doble clic y redimensionar desde los bordes. Al estrecharla, la barra lateral muestra solo iconos. Hay transiciones al abrir y cambiar de sección, animación de botones y de interruptores. Respeta la preferencia de Windows para animaciones de apertura y navegación. Los controles siguen siendo de Windows con un diseño inspirado en macOS; las fuentes de Apple se usan si están instaladas y, de lo contrario, se emplean las fuentes disponibles. El redondeado exterior nativo depende de la versión de Windows. No hay pestaña Capturar, vista previa ni botones de envío; la consulta se realiza con el atajo o desde el menú de la bandeja.

Se muestra únicamente la respuesta: letra/número y texto de la alternativa correcta, o las palabras/números que faltaban, en orden. El prompt prohíbe justificaciones, explicaciones y pasos. Si hay varios ejercicios, se numeran las respuestas. Si el texto es ilegible o faltan datos, el modelo debe responder «No se puede determinar». Comprueba siempre la respuesta.

En **Configuración**, el bloque **Captura y atajos de teclado** permite:

- Cambiar el atajo: haz clic en el campo, presiona una combinación con Ctrl, Alt, Mayús o Windows y otra tecla y pulsa **Guardar**. Por defecto es **Alt + Mayús + S**. Esc restaura el atajo anterior mientras editas. Si está ocupado o Windows lo reserva, se avisa y se conserva el atajo activo. El texto del programa y la bandeja muestran el atajo registrado.
- Configurar un segundo atajo para **respuesta recortada** (por defecto **Alt + Mayús + D**) y **Máximo de caracteres**, entre 1 y 10.000; por defecto 10. Este atajo toma otra captura y realiza una consulta normal, pero muestra solo los primeros N caracteres de la respuesta recibida, sin añadir puntos suspensivos. Espacios, signos y saltos de línea cuentan; acentos combinados y emojis se conservan completos. El atajo de respuesta completa sigue mostrando todo el texto. No se cambia el prompt, razonamiento ni consumo por usar el modo recortado. Los errores se muestran completos. Los dos atajos deben ser distintos; un conflicto conserva los atajos que ya funcionaban y también puedes intercambiar sus combinaciones. El límite y los dos atajos se guardan entre sesiones.
- Elegir **Pantalla completa** para capturar todo el monitor seleccionado.
- Elegir **Área guardada** y pulsar **Elegir área**. Arrastra el rectángulo sobre el monitor; Esc cancela sin sustituir el área anterior. La selección se guarda inmediatamente y las próximas capturas reutilizan el rectángulo sin pedir una nueva selección. Seleccionar el área no la envía al modelo.

Las dos combinaciones, el límite de texto, el modo y las coordenadas se guardan para los siguientes inicios. Las coordenadas son relativas al monitor: si cambias de monitor o su resolución, se pide seleccionar el área nuevamente. Pasar a pantalla completa conserva el área guardada. Si seleccionas Área guardada sin haber definido una, no se toma ni se envía una captura hasta que la configures.

Puedes configurar la esquina y tamaño de la respuesta. El atajo captura y envía siempre en una sola acción, incluso si la versión anterior tenía activada la revisión. La aplicación y respuesta anterior se ocultan antes de capturar. La ventana principal permanece oculta al terminar. Ambos modos mantienen el mismo panel transparente. La respuesta aparece como texto blanco con una sombra pequeña para facilitar la lectura, sobre fondo transparente, sin título, botones ni información del modelo. No toma el foco y permite hacer clic en la ventana que está debajo. Cada respuesta sustituye a la anterior en el mismo panel. Mientras una consulta está en curso, otras pulsaciones se ignoran para evitar envíos duplicados. No hay reintentos automáticos: una consulta posterior puede tener otra respuesta y otro cargo. Los errores se muestran como texto; si falta la clave o el área, se abre Configuración para corregirlo.

**Ocultar** deja el programa junto al reloj. Doble clic en el icono para abrirlo; su menú permite capturar, ver, ocultar o copiar la respuesta y salir. Minimizar oculta el programa en la bandeja; cerrar la ventana principal lo termina.

## Claves y modelos

Cada proveedor tiene su propia clave y modelo. Cambiar de proveedor conserva la selección del otro. Las claves anteriores de OpenAI se mantienen al actualizar.

- [Crear clave de Gemini en Google AI Studio](https://aistudio.google.com/apikey).
- [Crear clave de OpenAI](https://platform.openai.com/api-keys).

Opciones económicas de Gemini incluidas:

| Modelo | Uso |
| --- | --- |
| `gemini-3.5-flash-lite` | Flash-Lite actual, recomendado por Google para nuevos proyectos económicos. |
| `gemini-3.1-flash-lite` | Otra opción económica para tareas simples con imágenes. |
| `gemini-2.5-flash-lite` | Menor costo, con acceso limitado a cuentas que ya lo usaban. |
| `gemini-3.5-flash` | Opción de mayor capacidad y costo cuando Lite no resuelve bien el ejercicio. |

Google limita el acceso a los modelos 2.5 a usuarios que ya los usaban y recomienda 3.5 Flash-Lite para nuevos proyectos. La disponibilidad y cuota dependen de tu cuenta. Consulta los [modelos](https://ai.google.dev/gemini-api/docs/models) y [precios oficiales de Gemini](https://ai.google.dev/gemini-api/docs/pricing), incluidas las condiciones del nivel gratuito y de pago.

OpenAI conserva `gpt-5.4-mini` como opción económica y `gpt-5.4` para preguntas más complejas. Se usa razonamiento bajo y respuestas breves. El modelo se elige de la lista; el selector no admite escritura. Si tenías un modelo personalizado guardado, seguirá apareciendo entre las opciones.

En Gemini 3.5/3.1 Flash-Lite y 3.5 Flash se configura razonamiento alto y resolución de imagen alta, independientemente de que la respuesta visible sea breve. Se mantiene temperatura 1.0, como recomienda Google para Gemini 3. En 2.5 Flash-Lite se activa un presupuesto de 8.192 tokens de razonamiento y resolución alta. Esto puede aumentar el consumo y tiempo y no garantiza respuestas correctas. Los modelos personalizados mantienen sus parámetros predeterminados. Consulta la [configuración oficial de razonamiento](https://ai.google.dev/gemini-api/docs/generate-content/thinking), [resolución de imágenes](https://ai.google.dev/gemini-api/docs/generate-content/media-resolution) y [temperatura recomendada](https://ai.google.dev/gemini-api/docs/generate-content/gemini-3).

## Datos y consumo

- Solo se usa la clave del proveedor seleccionado: ninguna clave se envía al otro proveedor.
- **Recordar las claves** guarda ambas cifradas con Windows DPAPI para tu usuario en `%LOCALAPPDATA%\CapturaTutor\settings.json`. Si desmarcas y guardas, se eliminan las copias persistentes. Sin esa opción, las claves permanecen en memoria durante la sesión.
- Las capturas no se guardan en disco durante el uso normal. Puedes copiar la respuesta desde el menú del icono junto al reloj.
- La imagen se envía directamente por HTTPS a `api.openai.com/v1/responses` o `generativelanguage.googleapis.com/v1beta/models/...:generateContent`. No hay servidor intermediario.
- OpenAI usa `store: false`. La retención y uso de datos dependen de las políticas y nivel de servicio del proveedor. El nivel gratuito de Gemini puede usar el contenido para mejorar sus productos, según sus condiciones.
- Cada consulta puede generar un cargo. Los tokens incluyen razonamiento cuando el proveedor lo informa y no equivalen a un precio final. El límite es 16.384 tokens en Gemini y 4.096 en OpenAI, incluidos los de razonamiento. La respuesta transparente no muestra métricas de consumo. Una respuesta de Gemini truncada por el límite se trata como error y no se presenta como solución.
- Límites de imagen de la aplicación: 20 MB en OpenAI y 14 MB en Gemini para permitir el tamaño adicional de Base64. Seleccionar solo el ejercicio mejora la legibilidad y reduce el tamaño.
- **Cancelar** deja de esperar; una solicitud ya enviada puede generar un cargo. Espera máxima de cinco minutos, sin capturas periódicas ni reintentos automáticos. El programa hace una solicitud por consulta; no multiplica las consultas para realizar votaciones.

## Verificación y compilación

Requiere .NET SDK 8 para compilar en Windows:

```powershell
.\build.ps1 -Test
```

Genera el ejecutable portable en `dist/interfaz-limpia`. Las pruebas locales usan respuestas simuladas; no requieren claves ni consumen API. Cubren ambos proveedores, autenticación independiente, imágenes, ambas tareas, razonamiento según modelo, rechazo de respuestas incompletas, errores de clave/cuota, cifrado de ambas claves y restauración de área/atajo. No comprueban que un modelo real acierte un ejercicio.

Para comprobar el escritorio en una sesión interactiva de Windows:

```powershell
$p = Start-Process .\dist\configuracion\CapturaTutor.exe -ArgumentList '--smoke-test' -WindowStyle Hidden -Wait -PassThru
Get-Content .\dist\configuracion\smoke-test-result.txt
$p.ExitCode
```

Comprueba los paneles de Configuración, controles propios de ventana, maximización/restauración, minimización a la bandeja y adaptación a ventana compacta, las dos tareas, el cambio de proveedor sin perder modelos, una captura de área restaurada, registro y sustitución de atajos globales, conservación del atajo previo ante conflictos y ambas esquinas. No envía imágenes ni altera la configuración guardada. Además comprueba el envío automático con respuestas simuladas, que no se abra la ventana principal ni se dupliquen solicitudes, la reutilización del mismo panel, transparencia, ausencia de activación y clics a través. Guarda `smoke-overlay.png`, `smoke-ui.png`, `smoke-config.png` y `smoke-compact.png` con vistas de la interfaz junto al ejecutable. Una consulta real requiere tu clave y acceso al modelo.

Para una evaluación real explícita se admite `--evaluate-image archivo.png --expected c --runs 3`, con `--crop x,y,ancho,alto` opcional. Usa el modelo Gemini y clave guardados, hace entre una y tres solicitudes facturables y escribe `evaluation-result.json`. La letra esperada solo se usa para comprobar el resultado localmente: no se incluye en el prompt. Este diagnóstico no se ejecuta durante el uso normal. Acertar unos ejemplos no demuestra ausencia de errores futuros.

Fuentes de implementación: [visión OpenAI](https://developers.openai.com/api/docs/guides/images-vision), [imágenes Gemini](https://ai.google.dev/gemini-api/docs/generate-content/image-understanding), [generateContent](https://ai.google.dev/api/generate-content), [claves Gemini](https://ai.google.dev/gemini-api/docs/api-key).
