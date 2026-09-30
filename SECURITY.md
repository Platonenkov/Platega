# Security policy

## Reporting a vulnerability

Do not open a public issue for a security problem. Report it privately through [GitHub security advisories](https://github.com/Platonenkov/Platega/security/advisories/new) with a description, the affected version, and steps to reproduce.

You will receive an answer within seven days.

## Supported versions

Security fixes are released for the latest published version.

## Handling credentials

- Never commit the Platega API key or the payout secret. Use user secrets during development and environment variables or a secret store in production.
- Callbacks are authenticated only by the static `X-Secret` header. Re-read the transaction status through the API before fulfilling an order.
- If the key may have leaked, regenerate it in the Platega merchant cabinet. Check the callback URL there first: Platega sends the key to that address with every callback.
