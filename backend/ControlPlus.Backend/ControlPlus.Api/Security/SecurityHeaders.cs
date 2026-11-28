namespace ControlPlus.Api.Security;

/// <summary>Cabeceras de protección del navegador que la API agrega a todas sus respuestas.</summary>
public static class SecurityHeaders
{
    /// <summary>
    /// Política de contenido (auditoría ZAP del 27/11/2026, ZAP-PAS-001). La pantalla compilada no lleva scripts en
    /// línea (angular.json desactiva inlineCritical y el CI lo comprueba), así que script-src se limita a 'self'.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "font-src 'self' data:; connect-src 'self'; frame-src 'self'; frame-ancestors 'none'; base-uri 'self'; " +
        "form-action 'self'; object-src 'none'";
}
