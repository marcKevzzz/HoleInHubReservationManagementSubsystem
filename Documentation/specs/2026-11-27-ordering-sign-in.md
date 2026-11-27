# Continue with Hole In Hub

Proposed integration contract for the Ordering team. Shared sign-in requires an OpenID Connect provider in Hole In Hub and a client in Ordering. The existing customer profile API already supports a separate, one-time approval for sharing contact details.

## Customer journey

1. The customer selects **Continue with Hole In Hub** in Ordering.
2. Hole In Hub handles sign-in, registration, email verification, password recovery and Google sign-in when configured. Preserve the pending authorization request through these screens using a local continuation. Keep the existing account screens' local return URL validation.
3. Hole In Hub asks the verified customer to share their account identifier with Ordering. Cancellation returns the customer to Ordering without starting a session.
4. After approval, Ordering signs the customer in and saves their Hole In Hub account ID on each order. Updating an email address keeps previous orders linked.

## Contract for both teams

Use a maintained OpenID Connect implementation on each side. Register Ordering as a confidential web client per environment with an exact HTTPS callback URL and a dedicated server-side credential. Configure the trusted issuer explicitly and obtain endpoints and public signing keys through its discovery document.

Ordering requests scope `openid` using authorization code flow, PKCE S256, state and nonce. Hole In Hub restricts issuance to eligible, verified customer accounts. It issues a short-lived, single-use code bound to the client, callback and code challenge. Ordering redeems it on the server and validates state, signature, issuer, audience, expiration and nonce through its OIDC middleware before creating a local customer session.

Hole In Hub sets the ID token's `sub` to the stable customer account ID returned as `accountId` by the shared-profile API. Ordering identifies accounts by the verified issuer and subject. Keep email addresses, phone numbers, staff roles and reservation details outside this sign-in scope. If Ordering separately requests contact details, use the existing customer approval flow and require the returned accountId to match the signed-in subject before attaching them.

## Delivery and acceptance

Hole In Hub can implement discovery, authorization, token issuance and signing-key management and verify the flow with a local test client before the Ordering repository exists. Ordering will supply its callback URL for each environment and implement the button, callback and order ownership. Exchange client credentials privately.

Before rollout, exercise password and Google sign-in, new account verification, password recovery and cancellation from an Ordering checkout. Verify two orders around an email change retain the same owner. Confirm a mismatched callback, replayed code, invalid token or ineligible account cannot start an Ordering session.

Protocol references are [OpenID Connect Core](https://openid.net/specs/openid-connect-core-1_0.html) and [OAuth security best current practice](https://www.rfc-editor.org/rfc/rfc9700.html).
