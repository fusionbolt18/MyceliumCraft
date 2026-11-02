package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.core.component.DataComponents;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.Style;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.entity.EntityTypes;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.entity.player.Inventory;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.item.component.CustomData;
import net.minecraft.world.item.component.ItemLore;

/**
 * Your Mycelium resources as items in Minecraft's inventory, one slot each (your requests,
 * 2026-11-02; textures later): Rotten Log is a poisonous potato, Fresh Exoskeleton a nautilus shell,
 * Calcite Deposit tube coral, Flesh rotten flesh. Mycelium sends the list you carry (MycState
 * 0x74/0x78, ResourceBag.cs), each with its own id; this keeps exactly one item per id, marked in its
 * custom data, named as Mycelium names it, with a line saying what it's for, and never stacked.
 * Like the weapons, they're Mycelium's: they can't be eaten or placed.
 *
 * Throwing one (Q, as the weapons): the item flies through Minecraft's world, nobody can pick it up,
 * and its position goes to Mycelium every frame (Thrown block, flag THR_RESOURCE; MycWeapons
 * .thrownFrame), which takes it out of your bag and moves the real resource along. Once it has
 * rested on the ground for 5 ticks (or after 10 s) the item is removed and the real one stays on
 * Mycelium's ground as a pickup. While it flies its id counts as present, so it isn't put back.
 * Changes are made on the integrated server, twice a second or when Mycelium's list changes.
 */
public final class MycResources {
	private static final String KEY = "myceliumcraft_resource"; // the type, 1..4
	private static final String ID_KEY = "myceliumcraft_resource_id"; // Mycelium's id for this one
	private static final String[] NAMES = { "", "Rotten Log", "Fresh Exoskeleton", "Calcite Deposit", "Flesh" };
	private static final String[] FOR = { "", "sporelius", "toxitious", "costaalis", "gloomacea" }; // as Mycelium's pop-up says
	private static int[] lastIds = new int[0], lastTypes = new int[0];
	private static long nextSync;
	private static volatile boolean syncQueued;
	private static final Map<ItemEntity, Integer> RESTING = new java.util.WeakHashMap<>();

	private MycResources() {
	}

	private static Item item(int n) {
		return switch (n) {
			case 1 -> Items.POISONOUS_POTATO;
			case 2 -> Items.NAUTILUS_SHELL;
			case 3 -> Items.TUBE_CORAL;
			default -> Items.ROTTEN_FLESH;
		};
	}

	/** The resource type on a stack (0 = not one of ours). */
	public static int resource(ItemStack stack) {
		if (stack == null || stack.isEmpty()) {
			return 0;
		}
		CustomData data = stack.get(DataComponents.CUSTOM_DATA);
		return data == null || data.isEmpty() ? 0 : data.copyTag().getIntOr(KEY, 0);
	}

	/** Mycelium's id for the resource on a stack (0 = not one of ours, or made before ids). */
	public static int resourceId(ItemStack stack) {
		if (resource(stack) == 0) {
			return 0;
		}
		return stack.get(DataComponents.CUSTOM_DATA).copyTag().getIntOr(ID_KEY, 0);
	}

	/** Every frame (render thread): Mycelium's list into the inventory. */
	public static void frame(Minecraft minecraft, LocalPlayer player, boolean active, MycLink.MycState myc) {
		var server = minecraft.getSingleplayerServer();
		if (!active || server == null || player == null || player.isDeadOrDying()) {
			return;
		}
		int n = myc.resourceCount;
		int[] ids = Arrays.copyOf(myc.resourceIds, n), types = Arrays.copyOf(myc.resourceTypes, n);
		long now = System.currentTimeMillis();
		boolean changed = !Arrays.equals(ids, lastIds) || !Arrays.equals(types, lastTypes);
		if (changed) {
			MycLink.LOG.info("MyceliumCraft: resources: Mycelium carries {}", describe(ids, types));
			lastIds = ids;
			lastTypes = types;
		}
		if ((changed || now >= nextSync) && !syncQueued) {
			nextSync = now + 500;
			syncQueued = true;
			var uuid = player.getUUID();
			server.execute(() -> {
				try {
					ServerPlayer sp = server.getPlayerList().getPlayer(uuid);
					if (sp != null && !sp.isDeadOrDying()) {
						sync(sp, ids, types);
					}
				} finally {
					syncQueued = false;
				}
			});
		}
	}

	/** Exactly one item per resource in the list (server thread); an empty list takes them all away. */
	public static void sync(ServerPlayer sp, int[] ids, int[] types) {
		Map<Integer, Integer> want = new LinkedHashMap<>();
		for (int i = 0; i < ids.length; i++) {
			if (ids[i] != 0 && types[i] >= 1 && types[i] < NAMES.length) {
				want.put(ids[i], types[i]);
			}
		}
		Inventory inv = sp.getInventory();
		Set<Integer> seen = new HashSet<>();
		// Thrown ones flying or lying in the world are present: not made again.
		for (ItemEntity item : sp.level().getEntities(EntityTypes.ITEM, e -> resource(e.getItem()) != 0)) {
			seen.add(resourceId(item.getItem()));
		}
		int removed = 0, added = 0;
		for (int slot = 0; slot < inv.getContainerSize(); slot++) {
			ItemStack stack = inv.getItem(slot);
			int type = resource(stack);
			if (type == 0) {
				continue;
			}
			int id = resourceId(stack);
			Integer w = want.get(id);
			if (w == null || w != type || !stack.is(item(type)) || !seen.add(id)) {
				inv.setItem(slot, ItemStack.EMPTY);
				removed++;
				continue;
			}
			if (stack.getCount() != 1) {
				stack.setCount(1);
			}
		}
		// The one on the mouse cursor (an open inventory screen) counts too.
		ItemStack onCursor = sp.containerMenu.getCarried();
		if (resource(onCursor) != 0) {
			int id = resourceId(onCursor);
			if (!want.containsKey(id) || !seen.add(id)) {
				sp.containerMenu.setCarried(ItemStack.EMPTY);
				removed++;
			}
		}
		for (var e : want.entrySet()) {
			if (seen.contains(e.getKey())) {
				continue;
			}
			ItemStack stack = new ItemStack(item(e.getValue()));
			apply(stack, e.getValue(), e.getKey());
			if (inv.add(stack)) {
				added++;
			} else {
				MycLink.LOG.info("MyceliumCraft: resources: no room in the inventory for '{}' (#{})", NAMES[e.getValue()], e.getKey());
			}
		}
		if (removed + added > 0) {
			MycLink.LOG.info("MyceliumCraft: resources: inventory: {} added, {} removed (carrying {})", added, removed, want.size());
		}
	}

	/** Take them all away (server thread): before Minecraft's death drop. */
	public static void removeAll(ServerPlayer sp) {
		sync(sp, new int[0], new int[0]);
	}

	private static void apply(ItemStack stack, int n, int id) {
		CompoundTag tag = new CompoundTag();
		tag.putInt(KEY, n);
		tag.putInt(ID_KEY, id);
		stack.set(DataComponents.CUSTOM_DATA, CustomData.of(tag));
		stack.set(DataComponents.MAX_STACK_SIZE, 1); // one slot each
		stack.remove(DataComponents.FOOD);
		stack.remove(DataComponents.CONSUMABLE);
		stack.set(DataComponents.CUSTOM_NAME, Component.literal(NAMES[n]).withStyle(Style.EMPTY.withItalic(false)));
		List<Component> lore = new ArrayList<>();
		lore.add(Component.literal("Resource (Mycelium)").withStyle(Style.EMPTY.withItalic(false).withColor(0xAAAAAA)));
		lore.add(Component.literal("Used to grow and upgrade " + FOR[n] + " Spores").withStyle(Style.EMPTY.withItalic(false).withColor(0xAAAAAA)));
		stack.set(DataComponents.LORE, new ItemLore(lore));
	}

	/** Once: ours can't be placed (tube coral is a block); thrown ones can't be picked up, and go once they've landed. */
	public static void init() {
		net.fabricmc.fabric.api.event.player.UseBlockCallback.EVENT.register((player, level, hand, hit) ->
			resource(player.getItemInHand(hand)) != 0 ? InteractionResult.FAIL : InteractionResult.PASS);
		net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents.END_SERVER_TICK.register(server -> {
			for (var level : server.getAllLevels()) {
				for (ItemEntity item : level.getEntities(EntityTypes.ITEM, e -> resource(e.getItem()) != 0)) {
					item.setNeverPickUp(); // it's the real resource in Mycelium now
					boolean resting = item.onGround() && item.getDeltaMovement().lengthSqr() < 1e-4;
					int ticks = resting ? RESTING.getOrDefault(item, 0) + 1 : 0;
					RESTING.put(item, ticks);
					if (ticks >= 5 || item.getAge() > 200) {
						int type = Math.min(NAMES.length - 1, resource(item.getItem()));
						MycLink.LOG.info("MyceliumCraft: resources: thrown '{}' (#{}) {} at ({}, {}, {}); its item is removed, the resource stays in Mycelium",
							NAMES[type], resourceId(item.getItem()), ticks >= 5 ? "landed" : "still flying after 10 s",
							String.format("%.2f", item.getX()), String.format("%.2f", item.getY()), String.format("%.2f", item.getZ()));
						item.discard();
						RESTING.remove(item);
					}
				}
			}
		});
	}

	private static String describe(int[] ids, int[] types) {
		if (ids.length == 0) {
			return "nothing";
		}
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < ids.length; i++) {
			int t = types[i] >= 1 && types[i] < NAMES.length ? types[i] : 0;
			sb.append(i > 0 ? ", " : "").append(t == 0 ? "?" : NAMES[t]).append(" #").append(ids[i]);
		}
		return ids.length + ": " + sb;
	}
}
