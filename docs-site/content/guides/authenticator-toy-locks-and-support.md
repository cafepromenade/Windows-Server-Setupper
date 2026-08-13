# Authenticator, toy locks, and Support Tickets
Category: Local tools
Suggested: privacy-local-data-and-history,accessibility-and-responsive-use,local-ollama-manager

## In-memory TOTP

The static site offers a user-initiated RFC 6238 calculation using SHA-1, six digits, and a 30-second period. The Base32 secret remains in memory and is never written to site storage. QR import, secure credential-vault storage, and secrets export are unavailable here.

## Toy locks

Each locked tab has its own locally hashed password and a separate 15-minute unlock window. A toy lock is a self-imposed speed bump, not security, protection, or encryption. Clearing this site's browser storage resets the lock list.

## Support Tickets

Support Tickets is an entirely local joke desk with a real disclosure: nothing is sent, no external ticket exists, no network request is made, no data is collected, and nobody is reading it. The site gives browser storage-reset instructions but does not delete data in-app.
