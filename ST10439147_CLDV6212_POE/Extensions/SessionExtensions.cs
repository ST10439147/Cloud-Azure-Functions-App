// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
// CourseCode: CLDV6212
// POE Part: 3 - Session Extensions

using System.Text.Json;

namespace ST10439147_CLDV6212_POE.Extensions
{
    /// <summary>
    /// Extension methods for ISession to simplify storing/retrieving complex objects
    /// </summary>
    public static class SessionExtensions
    {
        /// <summary>
        /// Store an object in session as JSON
        /// </summary>
        public static void SetObjectAsJson(this ISession session, string key, object value)
        {
            var json = JsonSerializer.Serialize(value);
            session.SetString(key, json);
        }

        /// <summary>
        /// Retrieve an object from session JSON
        /// </summary>
        public static T? GetObjectFromJson<T>(this ISession session, string key) where T : class
        {
            var json = session.GetString(key);

            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(json);
        }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//