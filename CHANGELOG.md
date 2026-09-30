# Changelog

All notable changes to this project are documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-09-30

First public release.

### Added

- `Platega.Client`: payments with the universal or a fixed payment form, transaction status, H2H payment data, CSV/Excel/JSON exports with status filters, balances, refund availability and cancellation, recurring SBP subscriptions, Payout API with HMAC-SHA256 signing and caller-owned idempotency keys, callback authentication and parsing.
- `Platega.Client.AspNetCore`: `MapPlategaCallback` endpoint with constant-time credential checks, reachability probe handling, body size limit, and status codes preserved for Platega retries.
- Parsed Platega error bodies on `PlategaApiException` (`ErrorCode`, `ErrorType`, `ErrorMessage`, `ErrorDetails`, `TraceId`).
- Targets .NET 8 and .NET 10.

### Known limitations

- SBP subscriptions, the Payout API and H2H were not enabled on the account used for live verification; their numeric subscription status codes and payout path signing are unverified. See [docs/design.md](docs/design.md#open-questions).

[0.1.0]: https://github.com/Platonenkov/Platega/releases/tag/v0.1.0
