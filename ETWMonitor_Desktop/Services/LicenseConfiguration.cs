namespace EtwMonitor.Desktop.Services
{
    /// <summary>
    /// Configuration for license validation
    /// Contains the public key for verifying license signatures
    /// </summary>
    public static class LicenseConfiguration
    {
        /// <summary>
        /// Public key for license validation
        /// REPLACE THIS WITH YOUR ACTUAL PUBLIC KEY FROM public_key.txt
        /// 
        /// To get your public key:
        /// 1. Run your license generator with --generate-keys (if you haven't already)
        /// 2. Open public_key.txt
        /// 3. Copy the entire base64 string
        /// 4. Paste it here between the quotes
        /// 
        /// IMPORTANT: Never include the private key in your application!
        /// </summary>
        public const string PublicKey = "MIIBCgKCAQEAlmb+F+Z9vItfPKnZsurNgE5w2uHSdoYL1vDZA45y9h9iasDcEStXlJ22qIk2WmfA+n53n6FjErn5IrXpynJKP4x1rqtirgesgW6FgHMQW9Dp3mBhrpcqFNzZm97PzZT0FRTKuit5o5YcBkoNtV8viCeZhOWJoc5ycOsnLBHh8NHu8L7Q9l2F36rOygshGGhWl9+EPPL65hVXBt8yNUuAu6TVWS7N+4s7rvRpiq4WC+nBLwRZxG2OX5aYFF/VrNDBy2pUnxCYz7GRjAS+DmRcqbuc9hFs7Hezn7bHfXTxglTwlDE9DEDRLp9lXl8HEqtuH7l3vFNrTQkTvR/Q9Fo6mQIDAQAB";
        
        // Example format (this is not a real key, just showing the format):
        // public const string PublicKey = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA...";
    }
}
