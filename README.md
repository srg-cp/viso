# Viso

Aplicación de escritorio para Windows que captura un ejercicio, lo consulta con OpenAI o Gemini y muestra la respuesta en una esquina de la pantalla.

## Requisitos

- Windows 10 u 11 de 64 bits.
- Conexión a Internet y una API key del proveedor elegido.
- .NET SDK 8 para compilar. El ejecutable generado incluye .NET.

## Compilar

```powershell
.\build.ps1 -Test
```

El ejecutable queda en `dist/Viso.exe`. La opción `-Test` ejecuta pruebas locales con respuestas simuladas; no requiere claves ni consume API.

## Configurar

1. Abre Viso y entra en **Modelo de IA**. Elige el proveedor y modelo, añade tu API key y pulsa **Guardar**.
2. En **General**, selecciona el tipo de enunciado.
3. En **Captura y atajos**, elige el monitor y la captura completa o un área guardada.
4. Pulsa **Ocultar** y usa uno de los atajos.

| Atajo predeterminado | Resultado |
| --- | --- |
| Alt + Mayús + S | Respuesta completa |
| Alt + Mayús + D | Primeros N caracteres de la respuesta |

Los dos atajos son configurables. El límite incluye espacios y signos y solo afecta al texto mostrado; la consulta al modelo es la misma.

En **Respuesta** puedes elegir la esquina y el tamaño del texto. El interruptor de la barra lateral cambia entre modo claro y oscuro. Pulsa **Guardar** para conservar los ajustes.

El programa permanece en la bandeja junto al reloj. Su menú permite abrir los ajustes, capturar, copiar u ocultar la respuesta y salir. El botón rojo cierra la aplicación.

## Claves y privacidad

Puedes crear una clave en [OpenAI](https://platform.openai.com/api-keys) o [Google AI Studio](https://aistudio.google.com/apikey). La disponibilidad, los límites y el costo dependen de tu cuenta y del modelo.

- Las capturas se envían directamente al proveedor seleccionado por HTTPS. No hay servidor intermediario.
- Las capturas se mantienen en memoria durante el uso normal.
- **Recordar claves** las guarda cifradas con Windows DPAPI en `%LOCALAPPDATA%\Viso\settings.json`. Al desactivar la opción y guardar, se eliminan las claves persistentes.
- OpenAI recibe las solicitudes con `store: false`. Consulta las condiciones de cada proveedor para conocer su retención y uso de datos.
- Cada consulta puede tener costo. El programa evita envíos simultáneos y no hace reintentos automáticos. Cancelar la espera no anula una solicitud que el proveedor ya haya recibido.
- Las respuestas pueden contener errores. El programa no garantiza que una solución sea correcta.

## Pruebas de escritorio

En una sesión interactiva de Windows:

```powershell
$p = Start-Process .\dist\Viso.exe -ArgumentList '--smoke-test' -Wait -PassThru
Get-Content .\dist\smoke-test-result.txt
$p.ExitCode
```

Estas pruebas comprueban los controles, los temas, los atajos, la captura de un área y la respuesta superpuesta. Usan consultas simuladas y no modifican la configuración guardada. Los informes y capturas de prueba quedan junto al ejecutable, fuera de Git.

Para evaluar una imagen con Gemini:

```powershell
.\dist\Viso.exe --evaluate-image ejercicio.png --expected c --runs 1
```

Este comando usa la clave de Gemini guardada y realiza una consulta real, que puede tener costo. Admite `--crop x,y,ancho,alto` y hasta tres ejecuciones. Guarda `evaluation-result.json`; la respuesta esperada se usa para comprobar el resultado y no se envía al modelo.
