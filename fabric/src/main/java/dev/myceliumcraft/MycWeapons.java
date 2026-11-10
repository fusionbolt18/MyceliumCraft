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
import net.minecraft.core.component.DataComponentType;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.Style;
import net.minecraft.resources.Identifier;
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
import net.minecraft.world.item.component.CustomModelData;
import net.minecraft.world.item.component.ItemAttributeModifiers;
import net.minecraft.world.item.component.ItemLore;

/**
 * Your Mycelium weapons as food items in Minecraft's inventory, by type (your choices, 2026-10-20/21;
 * the looks come later): Slash weapons are bread, Smash weapons beetroot, Stab weapons carrots. Below,
 * "carrot" means any of them. Mycelium sends the table (Weapons block, WeaponBag.cs); this keeps exactly one carrot per
 * weapon, marked with the weapon's id in its custom data:
 *  - its name, and attribute modifiers for the main hand: attack damage = the weapon's damage per
 *    swing in Mycelium / Mycelium's damageScale (so a full-strength hit does that damage there),
 *    attack speed = its swings per second; Minecraft's own base values replaced (the tooltip shows
 *    the totals);
 *  - a description: type, damage, swings per second, knockback, its attributes;
 *  - not food (it can't be eaten).
 * Rewritten when Mycelium's numbers change (levelling up, a stat attribute, Fungal Might). Carrots of
 * weapons Mycelium doesn't have any more are removed (a new run starts with only the Stick), and so
 * are copies. A new weapon's carrot is selected only when your hand holds none of your weapons
 * (Mycelium asks: WeaponBag.AskSelect); otherwise the hotbar stays where it is.
 *
 * Throwing one (your choice, 2026-10-21): the item flies through Minecraft's world as usual (its own
 * physics, on Mycelium's ground), nobody can pick it up, and its position goes to Mycelium every
 * frame (Thrown block), which shows the real weapon on the same path and takes it out of your bag.
 * Once it has rested on the ground for 5 ticks (or after 10 s) the item is removed, and the real
 * weapon stays on Mycelium's ground as a pickup. While it flies, its weapon counts as present, so it
 * isn't put back in the inventory.
 * The carrot in the main hand is sent back (McState), and the spore holds that weapon.
 * Changes are made on the integrated server; the client only reads and selects.
 */
public final class MycWeapons {
	private static final String ID_KEY = "myceliumcraft_weapon";
	private static final String STAMP_KEY = "myceliumcraft_stamp";
	private static final String[] TYPES = { "Slash", "Smash", "Stab" };
	// Better Combat presets in this mod's data (data/myceliumcraft/weapon_attributes), by type.
	private static final Identifier[] PRESETS = {
		Identifier.fromNamespaceAndPath("myceliumcraft", "slash"),
		Identifier.fromNamespaceAndPath("myceliumcraft", "smash"),
		Identifier.fromNamespaceAndPath("myceliumcraft", "stab"),
	};
	private static final Identifier REACH_ID = Identifier.fromNamespaceAndPath("myceliumcraft", "weapon_reach");
	private static final double BASE_REACH = 3.0; // Minecraft's entity interaction range
	private static final double REACH_SCALE = 1.25; // attack ranges 25% bigger than measured (your choice, 2026-10-21)
	private static DataComponentType<Identifier> presetComponent;
	private static boolean presetLooked;
	private static final MycLink.Weapon[] TABLE = new MycLink.Weapon[Proto.W_MAX];
	private static final MycLink.WeaponsHeader HEADER = new MycLink.WeaponsHeader();
	private static int count = -1, lastVersion = Integer.MIN_VALUE, lastSelectSeq = Integer.MIN_VALUE;
	private static int pendingSelect;
	private static long pendingSince, nextSync, nextLog;
	private static volatile boolean syncQueued;
	// Mycelium's table as last read, for the Blacksmith (MycForge) on the server thread.
	private static volatile List<MycLink.Weapon> latestTable = List.of();
	private static volatile float damageScale = 5f;

	static {
		for (int i = 0; i < TABLE.length; i++) {
			TABLE[i] = new MycLink.Weapon();
		}
	}

	private MycWeapons() {
	}

	/** The weapon id on a stack (0 = not one of ours). */
	public static int weaponId(ItemStack stack) {
		if (stack == null || stack.isEmpty() || !(stack.is(Items.BREAD) || stack.is(Items.BEETROOT) || stack.is(Items.CARROT))) {
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
		if (changed) {
			latestTable = snapshot(n);
		}
		// Into the inventory: on a change at once, otherwise twice a second (to catch drops, copies).
		if ((changed || now >= nextSync) && !syncQueued && !player.isDeadOrDying()) {
			nextSync = now + 500;
			syncQueued = true;
			List<MycLink.Weapon> want = snapshot(n);
			float scale = HEADER.damageScale > 0f ? HEADER.damageScale : 5f;
			damageScale = scale;
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
		// Mycelium asks to select a new weapon (only when the hand holds none of yours): select its carrot (once it's in the hotbar).
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
		// Blacksmith Ironspore: a forged weapon's stand-in becomes it once Mycelium has answered.
		MycForge.resolve(sp, want);
		Inventory inv = sp.getInventory();
		Set<Integer> seen = new HashSet<>();
		// Weapons in the Blacksmith's slots are present too (they go back to you when it closes).
		if (sp.containerMenu instanceof MycForge.ForgeMenu forge) {
			for (int i = 0; i < 3; i++) {
				int id = weaponId(forge.input(i));
				if (id != 0 && (!byId.containsKey(id) || !seen.add(id))) {
					forge.clearInput(i);
				}
			}
		}
		// Thrown weapons flying or lying in the world are present: not made again.
		for (ItemEntity item : sp.level().getEntities(EntityTypes.ITEM, e -> weaponId(e.getItem()) != 0)) {
			seen.add(weaponId(item.getItem()));
		}
		int removed = 0, updated = 0, added = 0;
		for (int slot = 0; slot < inv.getContainerSize(); slot++) {
			ItemStack stack = inv.getItem(slot);
			int id = weaponId(stack);
			if (id == 0) {
				continue;
			}
			MycLink.Weapon w = byId.get(id);
			if (w == null && MycForge.keeps(id)) {
				seen.add(id); // just forged: Minecraft's copy of the table hasn't caught up yet
				continue;
			}
			if (w == null || !seen.add(id)) {
				inv.setItem(slot, ItemStack.EMPTY);
				removed++;
				continue;
			}
			if (stack.getCount() != 1) {
				stack.setCount(1);
			}
			if (!stack.is(item(w.type))) {
				// The wrong item for its type (made before Slash became bread, Smash beetroot): swap it in place.
				stack = new ItemStack(item(w.type));
				inv.setItem(slot, stack);
				apply(stack, w, scale);
				updated++;
				continue;
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
		if (carriedId != 0 && !(MycForge.keeps(carriedId) && !byId.containsKey(carriedId))) {
			if (!byId.containsKey(carriedId) || !seen.add(carriedId)) {
				sp.containerMenu.setCarried(ItemStack.EMPTY);
				removed++;
			}
		}
		for (MycLink.Weapon w : want) {
			if (seen.contains(w.id) || MycForge.holdsBack(w.id)) {
				continue; // there already, or given to the Blacksmith
			}
			ItemStack stack = new ItemStack(item(w.type));
			apply(stack, w, scale);
			if (inv.add(stack)) {
				added++;
			} else {
				MycLink.LOG.info("MyceliumCraft: weapons: no room in the inventory for '{}' (#{})", w.name, w.id);
			}
		}
		if (removed + updated + added > 0) {
			MycLink.LOG.info("MyceliumCraft: weapons: inventory: {} added, {} rewritten, {} removed", added, updated, removed);
		}
	}

	/** The item for a weapon type: Slash bread, Smash beetroot, Stab carrot. */
	private static net.minecraft.world.item.Item item(int type) {
		return type == 0 ? Items.BREAD : type == 1 ? Items.BEETROOT : Items.CARROT;
	}

	private static final int[] THROWN_IDS = new int[Proto.THR_MAX], THROWN_FLAGS = new int[Proto.THR_MAX];
	private static final double[] THROWN_XYZ = new double[Proto.THR_MAX * 3];
	private static int lastThrownCount;

	/** Every frame (render thread): your weapon and resource items in the world, for Mycelium (Thrown block). */
	public static void thrownFrame(Minecraft minecraft, float partial) {
		int n = 0;
		if (minecraft.level != null) {
			for (var entity : minecraft.level.entitiesForRendering()) {
				if (n >= Proto.THR_MAX || !(entity instanceof ItemEntity item) || item.isRemoved()) {
					continue;
				}
				int id = weaponId(item.getItem()), kind = 0;
				if (id == 0) {
					id = MycResources.resourceId(item.getItem()); // a thrown resource (MycResources)
					kind = Proto.THR_RESOURCE;
				}
				if (id == 0) {
					continue;
				}
				var pos = item.getPosition(partial);
				THROWN_IDS[n] = id;
				THROWN_FLAGS[n] = kind | (item.onGround() && item.getDeltaMovement().lengthSqr() < 1e-4 ? Proto.THR_LANDED : 0);
				THROWN_XYZ[n * 3] = pos.x;
				THROWN_XYZ[n * 3 + 1] = pos.y;
				THROWN_XYZ[n * 3 + 2] = pos.z;
				n++;
			}
		}
		if (n != lastThrownCount) {
			MycLink.LOG.info("MyceliumCraft: weapons: {} thrown weapon(s) and resource(s) in the world", n);
			lastThrownCount = n;
		}
		MycLink.writeThrown(n, THROWN_IDS, THROWN_FLAGS, THROWN_XYZ);
	}

	private static final Map<ItemEntity, Integer> RESTING = new java.util.WeakHashMap<>();

	/** Every server tick: thrown weapon items can't be picked up, and go once they've landed. */
	public static void init() {
		net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents.END_SERVER_TICK.register(server -> {
			for (var level : server.getAllLevels()) {
				for (ItemEntity item : level.getEntities(EntityTypes.ITEM, e -> weaponId(e.getItem()) != 0)) {
					item.setNeverPickUp(); // it's the real weapon in Mycelium now
					boolean resting = item.onGround() && item.getDeltaMovement().lengthSqr() < 1e-4;
					int ticks = resting ? RESTING.getOrDefault(item, 0) + 1 : 0;
					RESTING.put(item, ticks);
					if (ticks >= 5 || item.getAge() > 200) {
						MycLink.LOG.info("MyceliumCraft: weapons: thrown #{} {} at ({}, {}, {}); its item is removed, the weapon stays in Mycelium",
							weaponId(item.getItem()), ticks >= 5 ? "landed" : "still flying after 10 s",
							String.format("%.2f", item.getX()), String.format("%.2f", item.getY()), String.format("%.2f", item.getZ()));
						item.discard();
						RESTING.remove(item);
					}
				}
			}
		});
	}

	/** A weapon from Mycelium's table as last read (any thread), or null. */
	public static MycLink.Weapon latest(int id) {
		for (MycLink.Weapon w : latestTable) {
			if (w.id == id) {
				return w;
			}
		}
		return null;
	}

	public static List<MycLink.Weapon> latestTable() {
		return latestTable;
	}

	/**
	 * The Blacksmith's result as it would be (MycForge): a weapon's numbers, look and lore written as
	 * on any weapon item, but marked as the preview (no weapon id), with these lines on top.
	 */
	public static void applyPreview(ItemStack stack, MycLink.Weapon w, CompoundTag mark, List<Component> top) {
		apply(stack, w, damageScale);
		stack.set(DataComponents.CUSTOM_DATA, CustomData.of(mark));
		ItemLore lore = stack.get(DataComponents.LORE);
		List<Component> lines = new ArrayList<>(top);
		if (lore != null) {
			lines.addAll(lore.lines());
		}
		stack.set(DataComponents.LORE, new ItemLore(lines));
	}

	/** Make an item weapon id's item (the Blacksmith's stand-in): it's rewritten with its numbers on the next sync. */
	public static void claim(ItemStack stack, int id) {
		CompoundTag tag = new CompoundTag();
		tag.putInt(ID_KEY, id);
		tag.putInt(STAMP_KEY, 0);
		stack.set(DataComponents.CUSTOM_DATA, CustomData.of(tag));
		stack.remove(DataComponents.LORE);
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
			c.reach = s.reach;
			c.name = s.name;
			c.attributes = s.attributes;
			c.key = s.key;
			list.add(c);
		}
		return list;
	}

	private static int stamp(MycLink.Weapon w, float scale) {
		return Objects.hash(Math.round(w.damage * 100), Math.round(w.perSecond * 1000), Math.round(w.knockback * 10), w.type, w.name, w.attributes, Math.round(scale * 100), Math.round(w.reach * 100), w.key, 5);
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
		var modifiers = ItemAttributeModifiers.builder()
			.add(Attributes.ATTACK_DAMAGE, new AttributeModifier(Item.BASE_ATTACK_DAMAGE_ID, points - 1.0, AttributeModifier.Operation.ADD_VALUE), EquipmentSlotGroup.MAINHAND)
			.add(Attributes.ATTACK_SPEED, new AttributeModifier(Item.BASE_ATTACK_SPEED_ID, Math.max(0.05, w.perSecond) - 4.0, AttributeModifier.Operation.ADD_VALUE), EquipmentSlotGroup.MAINHAND);
		// Better Combat's attack range is the player's entity interaction range when the held item
		// changes it (else its preset's range bonus): the weapon's reach in Mycelium (Minecraft's
		// own 3 blocks if never held yet), 25% bigger.
		double range = attackRange(w);
		if (range != BASE_REACH) {
			modifiers.add(Attributes.ENTITY_INTERACTION_RANGE, new AttributeModifier(REACH_ID, range - BASE_REACH, AttributeModifier.Operation.ADD_VALUE), EquipmentSlotGroup.MAINHAND);
		}
		stack.set(DataComponents.ATTRIBUTE_MODIFIERS, modifiers.build());
		setPreset(stack, w.type);
		// Its own look from our built-in resource pack (src/main/weapon-models.json, made by the
		// weaponResources task): the item definition picks the model by this string.
		String model = modelKey(w.key);
		if (model.isEmpty()) {
			stack.remove(DataComponents.CUSTOM_MODEL_DATA);
		} else {
			stack.set(DataComponents.CUSTOM_MODEL_DATA, new CustomModelData(List.of(), List.of(), List.of(model), List.of()));
		}
		List<Component> lore = new ArrayList<>();
		Style gray = Style.EMPTY.withItalic(false).withColor(0xAAAAAA);
		Style blue = Style.EMPTY.withItalic(false).withColor(0x5577FF);
		lore.add(Component.literal(TYPES[Math.max(0, Math.min(2, w.type))] + " weapon (Mycelium)").withStyle(gray));
		lore.add(Component.literal(String.format("Damage per swing: %.1f (x%.2f)", w.damage, w.damageMult)).withStyle(gray));
		lore.add(Component.literal(String.format("Swings per second: %.2f (x%.2f)", w.perSecond, w.speedMod)).withStyle(gray));
		lore.add(Component.literal(String.format("Knockback: %.1f", w.knockback)).withStyle(gray));
		lore.add(Component.literal(w.reach > 0.1f ? String.format("Reach: %.2f blocks", range) : String.format("Reach: %.2f blocks until first held", range)).withStyle(gray));
		for (String att : w.attributes.split("; ")) {
			if (!att.isBlank()) {
				lore.add(attributeLine(att, blue));
			}
		}
		stack.set(DataComponents.LORE, new ItemLore(lore));
	}

	// Mycelium's stat icons (font myceliumcraft:stat_icons, cut from its icon sheet when the jar is
	// built; see build.gradle's statIcons). Without them the stat names stay.
	private static final java.util.regex.Pattern STAT = java.util.regex.Pattern.compile("([+-]\\d+) (Primal|Sentience|Speed|Vitality)\\b");
	private static final boolean STAT_ICONS = MycWeapons.class.getResource("/assets/myceliumcraft/font/stat_icons.json") != null;
	private static final net.minecraft.network.chat.FontDescription ICON_FONT = new net.minecraft.network.chat.FontDescription.Resource(Identifier.fromNamespaceAndPath("myceliumcraft", "stat_icons"));
	private static boolean iconsLogged;

	/** An attribute's line ("Beefy: +3 Primal, +3 Vitality") with each stat name after an amount as its icon. */
	public static Component attributeLine(String text, Style style) {
		if (!iconsLogged) {
			iconsLogged = true;
			MycLink.LOG.info("MyceliumCraft: weapons: stat icons in tooltips: {}", STAT_ICONS ? "yes (Mycelium's)" : "no (the jar was built without Mycelium's icon sheet): stat names");
		}
		if (!STAT_ICONS) {
			return Component.literal(text).withStyle(style);
		}
		var line = Component.empty().withStyle(style);
		var m = STAT.matcher(text);
		int last = 0;
		while (m.find()) {
			line.append(Component.literal(text.substring(last, m.start(2))));
			String glyph = switch (m.group(2)) {
				case "Primal" -> "\uE000";
				case "Sentience" -> "\uE001";
				case "Speed" -> "\uE002";
				default -> "\uE003";
			};
			// White, so the icon keeps its own colours (font glyphs take the text colour).
			line.append(Component.literal(glyph).withStyle(Style.EMPTY.withFont(ICON_FONT).withColor(0xFFFFFF)));
			last = m.end(2);
		}
		line.append(Component.literal(text.substring(last)));
		return line;
	}

	/** A prefab name as our resource pack names it: lower case, every run of other characters one '_' (as build.gradle). */
	public static String modelKey(String key) {
		return key.toLowerCase(java.util.Locale.ROOT).replaceAll("[^a-z0-9]+", "_").replaceAll("^_+|_+$", "");
	}

	/** The attack range for Better Combat (blocks): the measured reach, or Minecraft's 3 blocks if not yet measured, 25% bigger. */
	private static double attackRange(MycLink.Weapon w) {
		return (w.reach > 0.1f ? w.reach : BASE_REACH) * REACH_SCALE;
	}

	/**
	 * Better Combat's "bettercombat:preset_id" component: which of our presets animates this item.
	 * Looked up by name, so nothing happens without Better Combat.
	 */
	@SuppressWarnings("unchecked")
	private static void setPreset(ItemStack stack, int type) {
		if (!presetLooked) {
			presetLooked = true;
			presetComponent = (DataComponentType<Identifier>) BuiltInRegistries.DATA_COMPONENT_TYPE.getValue(Identifier.fromNamespaceAndPath("bettercombat", "preset_id"));
			MycLink.LOG.info("MyceliumCraft: Better Combat's preset_id component {}", presetComponent != null ? "found: weapons get our Slash/Smash/Stab presets" : "not found: no Better Combat animations");
		}
		if (presetComponent != null) {
			stack.set(presetComponent, PRESETS[Math.max(0, Math.min(2, type))]);
		}
	}

	private static String describe(int n) {
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < n; i++) {
			MycLink.Weapon w = TABLE[i];
			sb.append(i > 0 ? ", " : "").append('#').append(w.id).append(' ').append(w.name)
				.append(String.format(" %.1f dmg %.2f/s reach %.2f (range %.2f) look '%s'", w.damage, w.perSecond, w.reach, attackRange(w), modelKey(w.key)));
		}
		return sb.toString();
	}

	/** The weapon id of the main-hand carrot, for McState (0 = none). */
	public static int handWeapon(LocalPlayer player) {
		return player == null ? 0 : weaponId(player.getMainHandItem());
	}
}
