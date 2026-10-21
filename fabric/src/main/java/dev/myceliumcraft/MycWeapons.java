package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import dev.myceliumcraft.link.Proto;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Set;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.core.component.DataComponents;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.Style;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.EntityTypes;
import net.minecraft.world.entity.EquipmentSlotGroup;
import net.minecraft.world.entity.ai.attributes.AttributeModifier;
import net.minecraft.world.entity.ai.attributes.Attributes;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.entity.player.Inventory;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.item.component.CustomData;
import net.minecraft.world.item.component.ItemAttributeModifiers;
import net.minecraft.world.item.component.ItemLore;

/**
 * Your Mycelium weapons as carrots in Minecraft's inventory (your choice, 2026-10-20; the looks come
 * later). Mycelium sends the table (Weapons block, WeaponBag.cs); this keeps exactly one carrot per
 * weapon, marked with the weapon's id in its custom data:
 *  - its name, and attribute modifiers for the main hand: attack damage = the weapon's damage per
 *    swing in Mycelium / Mycelium's damageScale (so a full-strength hit does that damage there),
 *    attack speed = its swings per second; Minecraft's own base values replaced (the tooltip shows
 *    the totals);
 *  - a description: type, damage, swings per second, knockback, its attributes;
 *  - not food (it can't be eaten).
 * Rewritten when Mycelium's numbers change (levelling up, a stat attribute, Fungal Might). Carrots of
 * weapons Mycelium doesn't have any more are removed (a new run starts with only the Stick), and so
 * are copies (a carrot dropped and picked up, or left on the ground: it's made again in the
 * inventory and the dropped one is removed). After a pickup Minecraft selects the new carrot.
 * The carrot in the main hand is sent back (McState), and the spore holds that weapon.
 * Changes are made on the integrated server; the client only reads and selects.
 */
public final class MycWeapons {
	private static final String ID_KEY = "myceliumcraft_weapon";
	private static final String STAMP_KEY = "myceliumcraft_stamp";
	private static final String[] TYPES = { "Slash", "Smash", "Stab" };
	private static final MycLink.Weapon[] TABLE = new MycLink.Weapon[Proto.W_MAX];
	private static final MycLink.WeaponsHeader HEADER = new MycLink.WeaponsHeader();
	private static int count = -1, lastVersion = Integer.MIN_VALUE, lastSelectSeq = Integer.MIN_VALUE;
	private static int pendingSelect;
	private static long pendingSince, nextSync, nextLog;
	private static volatile boolean syncQueued;

	static {
		for (int i = 0; i < TABLE.length; i++) {
			TABLE[i] = new MycLink.Weapon();
		}
	}

	private MycWeapons() {
	}

	/** The weapon id on a stack (0 = not one of ours). */
	public static int weaponId(ItemStack stack) {
		if (stack == null || stack.isEmpty() || !stack.is(Items.CARROT)) {
			return 0;
		}
		CustomData data = stack.get(DataComponents.CUSTOM_DATA);
		if (data == null || data.isEmpty()) {
			return 0;
		}
		return data.copyTag().getIntOr(ID_KEY, 0);
	}

	/** Every frame on the render thread: read Mycelium's table, keep the inventory in step, select after a pickup. */
	public static void frame(Minecraft minecraft, LocalPlayer player, boolean active) {
		var server = minecraft.getSingleplayerServer();
		if (!active || server == null || player == null) {
			return;
		}
		int n = MycLink.readWeapons(TABLE, HEADER);
		if (n < 0) {
			return;
		}
		long now = System.currentTimeMillis();
		boolean changed = n != count || HEADER.version != lastVersion;
		if (changed) {
			MycLink.LOG.info("MyceliumCraft: weapons: Mycelium has {} weapon(s) (table version {}){}", n, HEADER.version, n > 0 ? ": " + describe(n) : "");
		}
		count = n;
		lastVersion = HEADER.version;
		// Into the inventory: on a change at once, otherwise twice a second (to catch drops, copies).
		if ((changed || now >= nextSync) && !syncQueued && !player.isDeadOrDying()) {
			nextSync = now + 500;
			syncQueued = true;
			List<MycLink.Weapon> want = snapshot(n);
			float scale = HEADER.damageScale > 0f ? HEADER.damageScale : 5f;
			var uuid = player.getUUID();
			server.execute(() -> {
				try {
					ServerPlayer sp = server.getPlayerList().getPlayer(uuid);
					if (sp != null && !sp.isDeadOrDying()) {
						sync(sp, want, scale);
					}
				} finally {
					syncQueued = false;
				}
			});
		}
		// Mycelium picked a weapon up: select its carrot (once it's in the hotbar).
		if (HEADER.selectSeq != lastSelectSeq) {
			if (lastSelectSeq != Integer.MIN_VALUE && HEADER.selectId != 0) {
				pendingSelect = HEADER.selectId;
				pendingSince = now;
			}
			lastSelectSeq = HEADER.selectSeq;
		}
		if (pendingSelect != 0) {
			Inventory inv = player.getInventory();
			for (int slot = 0; slot < Inventory.SELECTION_SIZE; slot++) {
				if (weaponId(inv.getItem(slot)) == pendingSelect) {
					inv.setSelectedSlot(slot);
					MycLink.LOG.info("MyceliumCraft: weapons: selected #{} in hotbar slot {} (just picked up)", pendingSelect, slot + 1);
					pendingSelect = 0;
					break;
				}
			}
			if (pendingSelect != 0 && now - pendingSince > 3000) {
				MycLink.LOG.info("MyceliumCraft: weapons: #{} isn't in the hotbar (full?); not selected", pendingSelect);
				pendingSelect = 0;
			}
		}
		if (now >= nextLog) {
			nextLog = now + 10000;
			MycLink.LOG.info("MyceliumCraft: weapons: {} weapon(s); main hand #{}, the spore holds #{}", n, weaponId(player.getMainHandItem()), HEADER.heldId);
		}
	}

	/**
	 * Make the inventory hold exactly one up-to-date carrot per weapon (server thread). Also used with
	 * an empty list to take them all away before Minecraft's death drop.
	 */
	public static void sync(ServerPlayer sp, List<MycLink.Weapon> want, float scale) {
		Map<Integer, MycLink.Weapon> byId = new HashMap<>();
		for (MycLink.Weapon w : want) {
			byId.put(w.id, w);
		}
		Inventory inv = sp.getInventory();
		Set<Integer> seen = new HashSet<>();
		int removed = 0, updated = 0, added = 0;
		for (int slot = 0; slot < inv.getContainerSize(); slot++) {
			ItemStack stack = inv.getItem(slot);
			int id = weaponId(stack);
			if (id == 0) {
				continue;
			}
			MycLink.Weapon w = byId.get(id);
			if (w == null || !seen.add(id)) {
				inv.setItem(slot, ItemStack.EMPTY);
				removed++;
				continue;
			}
			if (stack.getCount() != 1) {
				stack.setCount(1);
			}
			int stamp = stamp(w, scale);
			if (stack.get(DataComponents.CUSTOM_DATA).copyTag().getIntOr(STAMP_KEY, 0) != stamp) {
				apply(stack, w, scale);
				updated++;
			}
		}
		// The carrot on the mouse cursor (an open inventory screen) counts too.
		ItemStack carried = sp.containerMenu.getCarried();
		int carriedId = weaponId(carried);
		if (carriedId != 0) {
			if (!byId.containsKey(carriedId) || !seen.add(carriedId)) {
				sp.containerMenu.setCarried(ItemStack.EMPTY);
				removed++;
			}
		}
		for (MycLink.Weapon w : want) {
			if (seen.contains(w.id)) {
				continue;
			}
			ItemStack stack = new ItemStack(Items.CARROT);
			apply(stack, w, scale);
			if (inv.add(stack)) {
				added++;
			} else {
				MycLink.LOG.info("MyceliumCraft: weapons: no room in the inventory for '{}' (#{})", w.name, w.id);
			}
		}
		// Carrots lying in the world (dropped, or left by a death before this): gone, they live in the inventory.
		int discarded = 0;
		for (ItemEntity item : sp.level().getEntities(EntityTypes.ITEM, e -> weaponId(e.getItem()) != 0)) {
			item.discard();
			discarded++;
		}
		if (removed + updated + added + discarded > 0) {
			MycLink.LOG.info("MyceliumCraft: weapons: inventory: {} added, {} rewritten, {} removed, {} on the ground removed", added, updated, removed, discarded);
		}
	}

	/** Take every weapon carrot away (server thread): before Minecraft's death drop. */
	public static void removeAll(ServerPlayer sp) {
		sync(sp, List.of(), 5f);
	}

	private static List<MycLink.Weapon> snapshot(int n) {
		List<MycLink.Weapon> list = new ArrayList<>(n);
		for (int i = 0; i < n; i++) {
			MycLink.Weapon s = TABLE[i], c = new MycLink.Weapon();
			c.id = s.id;
			c.type = s.type;
			c.damage = s.damage;
			c.perSecond = s.perSecond;
			c.knockback = s.knockback;
			c.damageMult = s.damageMult;
			c.speedMod = s.speedMod;
			c.name = s.name;
			c.attributes = s.attributes;
			list.add(c);
		}
		return list;
	}

	private static int stamp(MycLink.Weapon w, float scale) {
		return Objects.hash(Math.round(w.damage * 100), Math.round(w.perSecond * 1000), Math.round(w.knockback * 10), w.type, w.name, w.attributes, Math.round(scale * 100), 1);
	}

	/** Write a weapon onto a carrot. */
	private static void apply(ItemStack stack, MycLink.Weapon w, float scale) {
		CompoundTag tag = new CompoundTag();
		tag.putInt(ID_KEY, w.id);
		tag.putInt(STAMP_KEY, stamp(w, scale));
		stack.set(DataComponents.CUSTOM_DATA, CustomData.of(tag));
		stack.remove(DataComponents.FOOD);
		stack.remove(DataComponents.CONSUMABLE);
		stack.set(DataComponents.CUSTOM_NAME, Component.literal(w.name.isEmpty() ? "Weapon" : w.name).withStyle(Style.EMPTY.withItalic(false)));
		// Minecraft's attack = the player's base (damage 1, speed 4) + these, in the main hand.
		double points = w.damage / scale;
		stack.set(DataComponents.ATTRIBUTE_MODIFIERS, ItemAttributeModifiers.builder()
			.add(Attributes.ATTACK_DAMAGE, new AttributeModifier(Item.BASE_ATTACK_DAMAGE_ID, points - 1.0, AttributeModifier.Operation.ADD_VALUE), EquipmentSlotGroup.MAINHAND)
			.add(Attributes.ATTACK_SPEED, new AttributeModifier(Item.BASE_ATTACK_SPEED_ID, Math.max(0.05, w.perSecond) - 4.0, AttributeModifier.Operation.ADD_VALUE), EquipmentSlotGroup.MAINHAND)
			.build());
		List<Component> lore = new ArrayList<>();
		Style gray = Style.EMPTY.withItalic(false).withColor(0xAAAAAA);
		Style blue = Style.EMPTY.withItalic(false).withColor(0x5577FF);
		lore.add(Component.literal(TYPES[Math.max(0, Math.min(2, w.type))] + " weapon (Mycelium)").withStyle(gray));
		lore.add(Component.literal(String.format("Damage per swing: %.1f (x%.2f)", w.damage, w.damageMult)).withStyle(gray));
		lore.add(Component.literal(String.format("Swings per second: %.2f (x%.2f)", w.perSecond, w.speedMod)).withStyle(gray));
		lore.add(Component.literal(String.format("Knockback: %.1f", w.knockback)).withStyle(gray));
		for (String att : w.attributes.split("; ")) {
			if (!att.isBlank()) {
				lore.add(Component.literal(att).withStyle(blue));
			}
		}
		stack.set(DataComponents.LORE, new ItemLore(lore));
	}

	private static String describe(int n) {
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < n; i++) {
			MycLink.Weapon w = TABLE[i];
			sb.append(i > 0 ? ", " : "").append('#').append(w.id).append(' ').append(w.name)
				.append(String.format(" %.1f dmg %.2f/s", w.damage, w.perSecond));
		}
		return sb.toString();
	}

	/** The weapon id of the main-hand carrot, for McState (0 = none). */
	public static int handWeapon(LocalPlayer player) {
		return player == null ? 0 : weaponId(player.getMainHandItem());
	}
}
