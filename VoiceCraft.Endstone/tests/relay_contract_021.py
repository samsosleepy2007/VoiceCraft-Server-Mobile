from __future__ import annotations

import asyncio

import relay_contract as contract
from endstone_voicecraft.compat import CompatibleEndstoneRelayClient


# Reuse the protocol scenarios while forcing them through the exact relay client
# exported by the 0.2.1 compatibility plugin.
contract.EndstoneRelayClient = CompatibleEndstoneRelayClient


if __name__ == "__main__":
    asyncio.run(contract.main())
