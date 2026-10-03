namespace Viso;

internal static class TaskPrompts
{
    public const string Alternative = "Enunciado y alternativas";
    public const string Complete = "Completar el enunciado";
    public const string System = "Resuelve el ejercicio de la imagen. Responde en español. Muestra solo la respuesta final. Si falta información o el texto es ilegible, responde 'No se puede determinar'.";
    public static string ForTask(string task) => task == Complete
        ? "Completa los espacios del enunciado. Devuelve únicamente las palabras, números o expresiones que faltan, en orden. Si hay varias preguntas, numera sus respuestas."
        : "Selecciona la alternativa correcta. Devuelve solo su letra o número y su texto, sin explicación. Si hay varias preguntas, numera sus respuestas.";
}
