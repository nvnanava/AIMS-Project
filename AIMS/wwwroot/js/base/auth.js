// auth.js - Reusable authentication module

class AuthService {
    constructor() {
        this.tokenCache = null;
        this.tokenExpiry = null;
    }

    /**
     * Get access token (cached or fetch new)
     */
    async getAccessToken() {
        // Return cached token if still valid
        if (this.tokenCache && this.tokenExpiry && Date.now() < this.tokenExpiry) {
            return this.tokenCache;
        }

        // Fetch new token
        try {
            const response = await fetch('/api/token');
            if (!response.ok) {
                throw new Error(`Failed to get access token: ${response.status}`);
            }

            const data = await response.json();
            this.tokenCache = data.access_token;

            // Cache for 50 minutes (tokens usually valid for 60 minutes)
            this.tokenExpiry = Date.now() + (50 * 60 * 1000);

            return this.tokenCache;
        } catch (error) {
            console.error('Error getting access token:', error);
            throw error;
        }
    }

    /**
     * Clear cached token (useful for logout or errors)
     */
    clearToken() {
        this.tokenCache = null;
        this.tokenExpiry = null;
    }
    async fetch(url, options = {}) {
            const token = await this.getAccessToken();

            const headers = {
                'Content-Type': 'application/json',
                ...options.headers,
                'Authorization': `Bearer ${token}`
            };

           return await fetch(url, { ...options, headers });
    }
}

// Export a singleton instance
const authService = new AuthService();

export default authService;