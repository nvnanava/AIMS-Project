using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

namespace AIMS.Routing
{
    public sealed class AllowedAssetTypeConstraint : IRouteConstraint
    {
        private static readonly HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase)
        {
            "hardware",
            "software"
        };

        public bool Match(HttpContext? httpContext, IRouter? route, string routeKey,
                          RouteValueDictionary values, RouteDirection routeDirection)
        {
            if (!values.TryGetValue(routeKey, out var obj) || obj is null) return false;
            var type = obj.ToString();
            return !string.IsNullOrWhiteSpace(type) && _allowed.Contains(type);
        }
    }
}
