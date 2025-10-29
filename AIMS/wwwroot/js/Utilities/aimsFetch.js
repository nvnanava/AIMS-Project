// Utility function to perform fetch requests with deduplication and cancellation support.
// Uses AbortController to manage request cancellation.
// Handles TTL for caching responses.

// Default Time-To-Live for cache in seconds.
// We use a global constant of 30 seconds, but for fine tuning 
// we can set the TTL in the options parameters in specific requests.
const DEFAULT_TTL = 30;

//Global client-side caches
window.aimsFetchCache = window.aimsFetchCache || new Map(); //url -> { data, expiry }
window.inFlightRequests = window.inFlightRequests || new Map(); //url -> { promise, abortController }


async function aimsFetch(url, options = {}) {

    // ----- Create a cache key -----
    const u = new URL(url, window.location.origin);
    u.searchParams.delete("_v");           // normalize key by removing version param
    const cacheKey = u.toString();

    // ----- Set TTL to given param in options or set to global default -----
    const cacheTTL = options.ttl ?? DEFAULT_TTL;
    const method = (options.method ?? 'GET').toUpperCase(); // we default to GET if no method is specified
    const now = Date.now();

    // ----- TTL Cache Check -----
    if (method === 'GET' && cacheTTL > 0) { //make sure we are only caching GET requests
        const cachedItem = aimsFetchCache.get(cacheKey);
        if (cachedItem && cachedItem.expiry > now) {
            return cachedItem.data; // Return cached data
        }
    }

    // ----- Deduplication Check -----
    // if there's already a request with the same URL in flight, the same promise is returned
    // This prevents duplicate requests for the same resource
    // to intentionally cancel an inflight request, use aimsFetch.abort(url)
    if (inFlightRequests.has(cacheKey)) {
        return inFlightRequests.get(cacheKey).promise;
    }

    // ----- Setup AbortController -----
    const abortController = new AbortController(); // Create a new AbortController for this request
    options = { ...options, signal: abortController.signal }; // Attach the signal to the fetch options

    // ----- merge headers -----
    const defaultHeaders = {
        'Content-Type': 'application/json',
        'Accept': 'application/json'
    };

    // create new headers object starting with an empty {}.
    // adds default headers, then either overwrites with any added headers or none if undefined.
    options.headers = Object.assign({}, defaultHeaders, options.headers ?? {});

    // ----- Perform Fetch -----
    const fetchPromise = (async () => {
        try {
            const response = await fetch(url, {
                ...options,
                cache: 'no-cache' // Ensure we are not using browser cache. We make our own boutique cache above.
            });

            if (response.status === 204) {
                return null; // no data
            }
            // ----- Handle HTTP Errors -----
            if (!response.ok) {
                let errorObj = { status: response.status, isValidation: false };

                try {
                    const json = await response.json();
                    errorObj.data = json;
                    if (response.status >= 400 && response.status < 500) {
                        errorObj.isValidation = true;
                    }
                } catch {
                    errorObj.data = { message: await response.text() };
                }

                throw errorObj;
            }
            const data = await response.json();

            // ----- Cache the response if GET and TTL > 0 -----
            if (method === 'GET' && cacheTTL > 0) {
                aimsFetchCache.set(cacheKey, {
                    data,
                    expiry: now + cacheTTL * 1000
                });
            }
            return data;
        } catch (error) {
            //silently fails aborted requests
            if (error.name === 'AbortError') {
                return { aborted: true };
            }
            throw error;
        } finally {
            //no memory leaks on unmounted requests.
            inFlightRequests.delete(cacheKey); // Clean up in-flight request entry
        }

    })();

    inFlightRequests.set(cacheKey, { promise: fetchPromise, abortController });
    return fetchPromise;
}

//
// Abort Helper functions that are attached to aimsFetch
//

//when needed instead of riding on existing inflight requests, 
//we can abort them instead using this function.
aimsFetch.abort = function (url) {
    const u = new URL(url, window.location.origin);
    u.searchParams.delete("_v");
    const cacheKey = u.toString();

    const entry = inFlightRequests.get(cacheKey);
    if (!entry) return false; // No in-flight request to abort

    entry.abortController.abort(); // Abort the request
    inFlightRequests.delete(cacheKey);
    return true; // Indicate that the request was aborted
}
aimsFetch.AbortAll = function () {
    for (const [_cacheKey, entry] of inFlightRequests) {
        entry.abortController.abort(); // Abort each request
    }
    inFlightRequests.clear(); // Clear all in-flight requests
};