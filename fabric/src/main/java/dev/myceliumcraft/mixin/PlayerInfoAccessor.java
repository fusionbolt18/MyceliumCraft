package dev.myceliumcraft.mixin;

import java.util.function.Supplier;
import net.minecraft.client.multiplayer.PlayerInfo;
import net.minecraft.world.entity.player.PlayerSkin;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Mutable;
import org.spongepowered.asm.mixin.gen.Accessor;

/** PlayerInfo's skin lookup, so the colony's rows get a built-in skin without asking Mojang (MycColony). */
@Mixin(PlayerInfo.class)
public interface PlayerInfoAccessor {
	@Mutable
	@Accessor("skinLookup")
	void myceliumcraft$setSkinLookup(Supplier<PlayerSkin> lookup);
}
