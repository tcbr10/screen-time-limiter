# Plan: managing PCs on other networks

Status: not started. This is the design to follow when the work begins. Nothing here is built yet.

## Goal

From one admin console, a parent sees and changes the limits of PCs that are somewhere else: another
house, another network, behind a home router. Each PC keeps enforcing on its own when it is offline.

## The constraint that shapes everything

A home PC can't be reached from outside: it sits behind a router, has no fixed address, and opening a
port on it would be both fragile and risky. So the PC must be the one that calls out. Every design
below follows from that.

## Recommended design: a small relay

```
 Admin console ──HTTPS/WebSocket──▶  Relay  ◀──WebSocket (outbound)── Service on each PC
   (parent)                        (cloud, tiny)                        (child's PC)
```

- **Relay**: a small always-on web service. Each PC's service opens an outbound WebSocket to it and
  keeps it open. The admin console connects to the same relay and addresses a PC by its `DeviceId`.
  The relay forwards messages and stores nothing but the list of paired devices.
- **PC side**: a new `RelayLink` background service next to `PipeServer`. It receives the same `Request`
  messages the pipe carries today and passes them to the existing `RequestRouter`.
- **Admin side**: the console gets a device list ("This PC", "Noa's laptop", …). Choosing a remote
  device swaps `PipeClient` for a `RelayClient` with the same `SendAsync(Request)` shape.

### Why the relay can be trusted with so little

Messages are signed and encrypted end to end between the admin console and the PC, so the relay only
moves sealed envelopes. If the relay is hacked, the attacker can drop or delay messages but can't read
settings or change a limit.

### Pairing a PC (one time, done at that PC by an administrator)

1. In the local admin console: "Allow remote management". The service creates a key pair for the PC
   and shows a short pairing code, valid for ten minutes.
2. On the parent's console: "Add a PC", type the code. The two sides exchange public keys through the
   relay, using the code to prove they are the right pair.
3. From then on the PC accepts remote requests only if they are signed by a paired admin key.
   "Remove remote access" in the local console deletes the pairing.

### What a remote admin may do

Same operations as the local console: read status and settings, save settings, grant extra time. Not
allowed remotely: setting the parent PIN and pairing new admins. Those stay local, so a stolen admin
key can't lock the owner out.

### Offline behaviour

- The PC enforces from its local settings file exactly as today. No connection is needed to enforce.
- Changes made while a PC is offline are queued in the console and sent when it reconnects, newest wins.
- Usage shown in the console carries a "last seen" time, so stale numbers are obvious.

## Alternatives considered

| Option | Why not first choice |
| --- | --- |
| Open a port on each PC and connect directly | Needs router setup at every location, exposes the PC to the internet. |
| Use a VPN such as Tailscale and connect directly inside it | No relay to run, and a good fit for a technical owner, but every PC and the parent's machine need the VPN installed and signed in. Worth offering as a "direct address" option, since it reuses the same message protocol. |
| Keep settings in a cloud database that PCs poll | Simple, but changes arrive late and the database holds everyone's settings unencrypted unless extra work is done. |

A LAN-only step (PCs in the same house finding each other) is possible as a first milestone, but it
doesn't serve the stated goal of other networks, so the plan goes straight to the relay.

## What the current code already does to prepare

- `RequestRouter` knows nothing about pipes. It takes a `Caller` and a `Request`, so a second transport
  plugs in without touching the rules.
- `Request` and `Response` are plain JSON, with no Windows types in them.
- `AppConfig.DeviceId` gives every PC a stable identity, and `SchemaVersion` allows the format to change.
- The parent PIN hash never leaves the service, so nothing new leaks when requests start crossing networks.

## Work to do, in order

1. **Protocol hardening**: add a request id, a timestamp, and a protocol version to `Request`; reject
   replays. Add a `GetDeviceInfo` operation (PC name, version, last seen).
2. **Keys and pairing**: generate and store the PC key pair (protected with Windows DPAPI under SYSTEM),
   pairing code flow, list of trusted admin keys in the config.
3. **Relay**: a small ASP.NET Core app with WebSocket endpoints for devices and admins, rate limits,
   and a health page. Deployable to any cheap host or run at home.
4. **`RelayLink` in the service**: outbound connection with reconnect and back-off; maps a verified
   remote admin to a `Caller` with admin rights minus the local-only operations.
5. **Console**: device list, "Add a PC", per-device connection state, queued changes for offline PCs.
6. **Security review and tests**: replayed, forged, and out-of-order messages; relay down; clock skew.

## Decisions to make when the work starts

- Where the relay runs: a hosted service (simplest for non-technical users, small monthly cost) or
  self-hosted (free, but someone has to keep it up).
- Whether one parent account can be shared by two parents, which needs more than one admin key per PC.
- Whether a phone app or web page for parents is wanted, which would favour a hosted relay with a web UI.
