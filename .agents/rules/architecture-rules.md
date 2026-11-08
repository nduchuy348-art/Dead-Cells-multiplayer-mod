# Architectural Rules for Dead Cells Multiplayer Mod

1. **Root-Cause First**: Never patch a symptom with a localized `if` check without verifying the entire data lifecycle.
2. **Authority Strictness**: Host owns world state, mob AI, seeds, and progression. Client owns local player input and prediction.
3. **Replication Purity**: Replicate state, not events with unconstrained side effects.
4. **Idempotence**: Network packets must be safe to process multiple times or out of order.
5. **No Desktop Pollution**: Never write scratch files or binaries to `C:\Users\Admin\Desktop\`.
6. **100% English**: All code, comments, docstrings, commits, and tests must be in English.
7. **Resource Bounds**: Keep CPU <= 80% and RAM <= 80%.
