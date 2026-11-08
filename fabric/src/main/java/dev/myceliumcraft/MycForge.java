package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import dev.myceliumcraft.link.Proto;
import java.util.ArrayList;
import java.util.List;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.MenuScreens;
import net.minecraft.client.gui.screens.inventory.ItemCombinerScreen;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.core.Registry;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.Style;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.SimpleMenuProvider;
import net.minecraft.world.entity.EntityTypes;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.entity.player.Inventory;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.flag.FeatureFlags;
import net.minecraft.world.inventory.ContainerLevelAccess;
import net.minecraft.world.inventory.ItemCombinerMenu;
import net.minecraft.world.inventory.ItemCombinerMenuSlotDefinition;
import net.minecraft.world.inventory.MenuType;
import net.minecraft.world.inventory.Slot;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.component.CustomData;
import net.minecraft.world.item.component.ItemLore;
import net.minecraft.world.level.block.state.BlockState;

/**
 * Blacksmith Ironspore as Minecraft's smithing screen (your request, 2026-11-08; ForgeWatch.cs).
 * Interacting with him in Mycelium opens a screen with the smithing table's look: the first slot is
 * the weapon base, the next two the attribute donors, then the result.
 *
 * - The slots take only the weapons Mycelium says he takes (Forge block): not a Stick, nothing
 *   forged, nothing without an attribute. Each weapon is one item, so one can't go in twice.
 * - With all three in, the result slot shows the forged weapon as it will be: the base's item and
 *   look, and the first attribute of each donor (what Mycelium's ForgeShroom takes).
 * - Taking it sends the request to Mycelium, which forges it (WeaponBag.Forge). Until it answers,
 *   the taken item is a stand-in; the three weapons given stay gone. When Mycelium answers with the
 *   new weapon's id, the stand-in becomes that weapon (MycWeapons writes its numbers). If Mycelium
 *   refuses (or says nothing for 10 s), the stand-in goes and the three weapons come back.
 * - He forges once, as in Mycelium; the screen closes when there's no Blacksmith left to forge.
 * Server thread for the menu and the inventory; the client only opens the screen.
 */
public final class MycForge {
	public static final Identifier ID = Identifier.fromNamespaceAndPath("myceliumcraft", "blacksmith");
	private static final String PREVIEW_KEY = "myceliumcraft_forge_preview";
	private static final Component TITLE = Component.literal("Blacksmith Ironspore");
	public static MenuType<ForgeMenu> TYPE;
	private static final MycLink.Forge FORGE = new MycLink.Forge();
	// From Mycelium (render thread writes, server thread reads).
	private static volatile int[] eligible = new int[0];
	private static volatile boolean available;
	private static volatile int handledSeq, resultId;
	private static volatile String reason = "";
	private static int lastOpenSeq = Integer.MIN_VALUE;
	// The result's numbers, from Mycelium (it builds the forged weapon as it would be).
	private static volatile int previewHandled;
	private static volatile MycLink.Weapon previewWeapon; // for the ids below, once answered
	private static volatile int previewStamp; // changes when its numbers do: the result is made again
	private static int previewSent, previewAsked; // server thread
	private static int[] previewIds = new int[0];
	private static int previewShownStamp;
	private static boolean lastAvailable;
	private static int lastEligible = -1;
	// The request waiting for Mycelium (server thread).
	private static int pendingSeq, nextSeq;
	private static int[] pendingIds = new int[0];
	private static String pendingBase = "";
	private static long pendingSince;
	// After an answer, for a few seconds: the weapons given aren't put back, the new one isn't taken
	// away, while Minecraft's copy of Mycelium's table catches up (server thread).
	private static int[] goneIds = new int[0];
	private static int freshId;
	private static long guardUntil;

	private MycForge() {
	}

	/** Mod start (client): the menu type, its screen, the server tick. */
	public static void init() {
		TYPE = Registry.register(BuiltInRegistries.MENU, ResourceKey.create(Registries.MENU, ID), new MenuType<>(ForgeMenu::new, FeatureFlags.VANILLA_SET));
		MenuScreens.register(TYPE, ForgeScreen::new);
		ServerTickEvents.END_SERVER_TICK.register(MycForge::tick);
		MycLink.LOG.info("MyceliumCraft: forge: Blacksmith Ironspore's screen registered as {}", ID);
	}

	/** Every frame (render thread): read the Forge block, open the screen when you interact with him. */
	public static void frame(Minecraft minecraft, LocalPlayer player, boolean active) {
		if (!MycLink.readForge(FORGE)) {
			return;
		}
		int[] ids = java.util.Arrays.copyOf(FORGE.ids, FORGE.count);
		eligible = ids;
		available = active && (FORGE.flags & Proto.FG_AVAILABLE) != 0;
		handledSeq = FORGE.handled;
		resultId = FORGE.result;
		reason = FORGE.reason;
		previewHandled = FORGE.previewHandled;
		MycLink.Weapon p = FORGE.preview;
		int stamp = java.util.Objects.hash(FORGE.previewHandled, p.name, p.attributes, Math.round(p.damage * 100), Math.round(p.perSecond * 1000), Math.round(p.knockback * 10), Math.round(p.reach * 100));
		if (stamp != previewStamp) {
			MycLink.Weapon c = new MycLink.Weapon();
			c.type = p.type;
			c.damage = p.damage;
			c.perSecond = p.perSecond;
			c.knockback = p.knockback;
			c.damageMult = p.damageMult;
			c.speedMod = p.speedMod;
			c.reach = p.reach;
			c.name = p.name;
			c.attributes = p.attributes;
			c.key = p.key;
			previewWeapon = c.name.isEmpty() ? null : c;
			previewStamp = stamp;
			if (!c.name.isEmpty()) {
				MycLink.LOG.info("MyceliumCraft: forge: Mycelium's preview (#{}): '{}' {} dmg, {}/s, knockback {}, reach {} blocks; {}", FORGE.previewHandled, c.name,
					String.format("%.1f", c.damage), String.format("%.2f", c.perSecond), String.format("%.1f", c.knockback), String.format("%.2f", c.reach), c.attributes);
			}
		}
		if (available != lastAvailable || ids.length != lastEligible) {
			lastAvailable = available;
			lastEligible = ids.length;
			MycLink.LOG.info("MyceliumCraft: forge: a Blacksmith here {}; he takes {} of your weapons {}", available, ids.length, java.util.Arrays.toString(ids));
		}
		if (lastOpenSeq == Integer.MIN_VALUE) {
			lastOpenSeq = FORGE.openSeq;
			return;
		}
		if (FORGE.openSeq == lastOpenSeq) {
			return;
		}
		lastOpenSeq = FORGE.openSeq;
		var server = minecraft.getSingleplayerServer();
		if (!active || server == null || player == null) {
			MycLink.LOG.info("MyceliumCraft: forge: Mycelium asks for the Blacksmith's screen, but Minecraft isn't playing it (active {}, server {})", active, server != null);
			return;
		}
		var uuid = player.getUUID();
		server.execute(() -> {
			ServerPlayer sp = server.getPlayerList().getPlayer(uuid);
			if (sp == null || sp.isDeadOrDying()) {
				return;
			}
			if (sp.containerMenu instanceof ForgeMenu) {
				MycLink.LOG.info("MyceliumCraft: forge: the Blacksmith's screen is open already");
				return;
			}
			sp.openMenu(new SimpleMenuProvider((id, inv, pl) -> new ForgeMenu(id, inv), TITLE));
			MycLink.LOG.info("MyceliumCraft: forge: the Blacksmith's screen opened (Mycelium's #{}); he takes {}", lastOpenSeq, java.util.Arrays.toString(eligible));
		});
	}

	/** Does the Blacksmith take this item (a weapon of yours Mycelium says he takes)? */
	public static boolean accepts(ItemStack stack) {
		int id = MycWeapons.weaponId(stack);
		if (id == 0) {
			return false;
		}
		for (int e : eligible) {
			if (e == id) {
				return true;
			}
		}
		return false;
	}

	/** While a request waits, or just after an answer: this weapon isn't to be put back in the inventory. */
	public static boolean holdsBack(int id) {
		for (int g : pendingIds) {
			if (g == id) {
				return true;
			}
		}
		if (System.currentTimeMillis() < guardUntil) {
			for (int g : goneIds) {
				if (g == id) {
					return true;
				}
			}
		}
		return false;
	}

	/** Just after an answer: the new weapon's item isn't taken away while Minecraft's table catches up. */
	public static boolean keeps(int id) {
		return id != 0 && id == freshId && System.currentTimeMillis() < guardUntil;
	}

	/** The result: the forged weapon as it will be, or nothing. */
	static ItemStack preview(ItemStack base, ItemStack donor1, ItemStack donor2) {
		int b = MycWeapons.weaponId(base), d1 = MycWeapons.weaponId(donor1), d2 = MycWeapons.weaponId(donor2);
		if (b == 0 || d1 == 0 || d2 == 0 || b == d1 || b == d2 || d1 == d2 || pendingSeq != 0 || !available
			|| !accepts(base) || !accepts(donor1) || !accepts(donor2)) {
			return ItemStack.EMPTY;
		}
		MycLink.Weapon wb = MycWeapons.latest(b), w1 = MycWeapons.latest(d1), w2 = MycWeapons.latest(d2);
		ItemStack r = base.copyWithCount(1);
		CompoundTag tag = new CompoundTag();
		tag.putInt(PREVIEW_KEY, 1);
		r.set(DataComponents.CUSTOM_DATA, CustomData.of(tag));
		MycLink.Weapon numbers = previewFor(b, d1, d2);
		if (numbers != null) {
			// Mycelium's own forged weapon, as it would be: its name, numbers and attributes.
			Style gold = Style.EMPTY.withItalic(false).withColor(0xFFAA00);
			Style gray = Style.EMPTY.withItalic(false).withColor(0xAAAAAA);
			List<Component> top = new ArrayList<>();
			top.add(Component.literal("Forged by Blacksmith Ironspore").withStyle(gold));
			top.add(Component.literal("Base: " + (wb != null ? ForgeName.plain(wb.name, wb.attributes) + " (from " + wb.name + ")" : "?")).withStyle(gray));
			MycWeapons.applyPreview(r, numbers, tag, top);
			return r;
		}
		// Mycelium's name for it: the donors' attributes in front of the plain name (not the base's own).
		if (wb != null && w1 != null && w2 != null) {
			r.set(DataComponents.CUSTOM_NAME, Component.literal(ForgeName.forged(wb.name, wb.attributes, w1.attributes, w2.attributes)).withStyle(Style.EMPTY.withItalic(false)));
		}
		Style gold = Style.EMPTY.withItalic(false).withColor(0xFFAA00);
		Style gray = Style.EMPTY.withItalic(false).withColor(0xAAAAAA);
		Style blue = Style.EMPTY.withItalic(false).withColor(0x5577FF);
		List<Component> lore = new ArrayList<>();
		lore.add(Component.literal("Forged by Blacksmith Ironspore").withStyle(gold));
		lore.add(Component.literal("Base: " + (wb != null ? ForgeName.plain(wb.name, wb.attributes) + " (from " + wb.name + ")" : "?")).withStyle(gray));
		lore.add(MycWeapons.attributeLine(firstAttribute(w1), blue));
		lore.add(MycWeapons.attributeLine(firstAttribute(w2), blue));
		lore.add(Component.literal("Its own attributes are replaced by these").withStyle(gray));
		lore.add(Component.literal("Working out its numbers...").withStyle(gray));
		r.set(DataComponents.LORE, new ItemLore(lore));
		return r;
	}

	/** Mycelium's numbers for these three, once it has answered for them (any thread), or null. */
	private static MycLink.Weapon previewFor(int b, int d1, int d2) {
		int[] ids = previewIds;
		if (ids.length != 3 || ids[0] != b || ids[1] != d1 || ids[2] != d2 || previewSent == 0 || previewHandled != previewSent) {
			return null;
		}
		return previewWeapon;
	}

	/** Ask Mycelium for the forged weapon's numbers for these three (server thread), if not asked yet. */
	static void askPreview(int b, int d1, int d2) {
		int[] ids = previewIds;
		if (ids.length == 3 && ids[0] == b && ids[1] == d1 && ids[2] == d2) {
			return;
		}
		if (previewAsked == 0) {
			previewAsked = MycLink.forgePreviewSeq();
		}
		previewSent = ++previewAsked == 0 ? ++previewAsked : previewAsked;
		previewIds = new int[] { b, d1, d2 };
		MycLink.writeForgePreview(previewSent, b, d1, d2);
		MycLink.LOG.info("MyceliumCraft: forge: asking Mycelium for the result's numbers: #{} (base), #{}, #{} (preview #{})", b, d1, d2, previewSent);
	}

	/** A donor's first attribute, as its item lists it ("Name: description"). */
	private static String firstAttribute(MycLink.Weapon w) {
		if (w == null || w.attributes.isBlank()) {
			return "(an attribute)";
		}
		String first = w.attributes.split("; ")[0];
		return "+ " + first + " (from " + w.name + ")";
	}

	private static boolean isPreview(ItemStack stack) {
		if (stack == null || stack.isEmpty()) {
			return false;
		}
		CustomData data = stack.get(DataComponents.CUSTOM_DATA);
		return data != null && data.copyTag().getIntOr(PREVIEW_KEY, 0) != 0;
	}

	/** You took the result (server thread): ask Mycelium to forge. */
	static void taken(ServerPlayer sp, int base, int donor1, int donor2) {
		if (nextSeq == 0) {
			nextSeq = MycLink.forgeRequestSeq(); // carry on from what's in the shared memory
		}
		pendingSeq = ++nextSeq;
		if (pendingSeq == 0) {
			pendingSeq = ++nextSeq;
		}
		pendingIds = new int[] { base, donor1, donor2 };
		MycLink.Weapon wb = MycWeapons.latest(base);
		pendingBase = wb != null ? wb.name : "the weapon";
		pendingSince = System.currentTimeMillis();
		MycLink.writeForgeRequest(pendingSeq, base, donor1, donor2);
		MycLink.LOG.info("MyceliumCraft: forge: result taken; asking Mycelium to forge #{} (base) with #{} and #{} (request #{})", base, donor1, donor2, pendingSeq);
	}

	/**
	 * Server thread (every tick, and from MycWeapons.sync): once Mycelium has answered and the new
	 * weapon is in Minecraft's copy of its table, the stand-in becomes it; refused, it goes. Stand-ins
	 * with no request waiting go too.
	 */
	static void resolve(ServerPlayer sp, List<MycLink.Weapon> table) {
		if (pendingSeq != 0) {
			boolean answered = handledSeq == pendingSeq;
			boolean late = System.currentTimeMillis() - pendingSince > 10000;
			int id = answered ? resultId : 0;
			if (answered && id != 0 && !has(table, id) && !late) {
				return; // its table entry hasn't arrived yet
			}
			if (!answered && !late) {
				return;
			}
			String why = answered ? reason : "Mycelium didn't answer in 10 s";
			int changed = 0;
			Inventory inv = sp.getInventory();
			for (int slot = 0; slot < inv.getContainerSize(); slot++) {
				if (isPreview(inv.getItem(slot))) {
					changed++;
					if (id != 0) {
						MycWeapons.claim(inv.getItem(slot), id);
					} else {
						inv.setItem(slot, ItemStack.EMPTY);
					}
				}
			}
			if (isPreview(sp.containerMenu.getCarried())) {
				changed++;
				if (id != 0) {
					MycWeapons.claim(sp.containerMenu.getCarried(), id);
				} else {
					sp.containerMenu.setCarried(ItemStack.EMPTY);
				}
			}
			if (id != 0) {
				goneIds = pendingIds;
				freshId = id;
				guardUntil = System.currentTimeMillis() + 5000;
				MycLink.Weapon w = latestIn(table, id);
				sp.sendSystemMessage(Component.literal("Blacksmith Ironspore forged " + (w != null ? w.name : pendingBase) + "."));
				MycLink.LOG.info("MyceliumCraft: forge: request #{} answered: weapon #{}; {} stand-in item(s) became it", pendingSeq, id, changed);
			} else {
				goneIds = new int[0];
				freshId = 0;
				sp.sendSystemMessage(Component.literal("Blacksmith Ironspore can't forge that: " + why + "."));
				MycLink.LOG.info("MyceliumCraft: forge: request #{} refused ({}); {} stand-in item(s) taken away, the three weapons come back", pendingSeq, why, changed);
			}
			pendingSeq = 0;
			pendingIds = new int[0];
			return;
		}
		Inventory inv = sp.getInventory();
		for (int slot = 0; slot < inv.getContainerSize(); slot++) {
			if (isPreview(inv.getItem(slot))) {
				inv.setItem(slot, ItemStack.EMPTY);
				MycLink.LOG.info("MyceliumCraft: forge: a stand-in result with no request waiting taken away (slot {})", slot);
			}
		}
		if (isPreview(sp.containerMenu.getCarried())) {
			sp.containerMenu.setCarried(ItemStack.EMPTY);
			MycLink.LOG.info("MyceliumCraft: forge: a stand-in result on the cursor with no request waiting taken away");
		}
	}

	private static boolean has(List<MycLink.Weapon> table, int id) {
		return latestIn(table, id) != null;
	}

	private static MycLink.Weapon latestIn(List<MycLink.Weapon> table, int id) {
		for (MycLink.Weapon w : table) {
			if (w.id == id) {
				return w;
			}
		}
		return null;
	}

	private static void tick(MinecraftServer server) {
		for (ServerPlayer sp : server.getPlayerList().getPlayers()) {
			resolve(sp, MycWeapons.latestTable());
			// Mycelium's numbers for the result arrived or changed: make the result again.
			if (sp.containerMenu instanceof ForgeMenu menu && previewStamp != previewShownStamp) {
				previewShownStamp = previewStamp;
				menu.createResult();
			}
			if (sp.containerMenu instanceof ForgeMenu && !available && pendingSeq == 0 && System.currentTimeMillis() >= guardUntil) {
				sp.closeContainer();
				MycLink.LOG.info("MyceliumCraft: forge: the Blacksmith's screen closed: no Blacksmith left to forge here");
			}
		}
		// A stand-in thrown out of the inventory is no weapon: it goes.
		for (var level : server.getAllLevels()) {
			for (ItemEntity item : level.getEntities(EntityTypes.ITEM, e -> isPreview(e.getItem()))) {
				item.discard();
				MycLink.LOG.info("MyceliumCraft: forge: a thrown stand-in result removed");
			}
		}
	}

	/** The smithing table's menu, with our slots: base, donor, donor, result. */
	public static final class ForgeMenu extends ItemCombinerMenu {
		public ForgeMenu(int id, Inventory inventory) {
			super(TYPE, id, inventory, ContainerLevelAccess.NULL, ItemCombinerMenuSlotDefinition.create()
				.withSlot(0, 8, 48, MycForge::accepts)
				.withSlot(1, 26, 48, MycForge::accepts)
				.withSlot(2, 44, 48, MycForge::accepts)
				.withResultSlot(3, 98, 48)
				.build());
		}

		@Override
		protected boolean isValidBlock(BlockState state) {
			return true;
		}

		@Override
		protected boolean mayPickup(Player player, boolean hasItem) {
			return hasItem && pendingSeq == 0;
		}

		@Override
		public void createResult() {
			ItemStack b = inputSlots.getItem(0), d1 = inputSlots.getItem(1), d2 = inputSlots.getItem(2);
			ItemStack r = preview(b, d1, d2);
			if (!r.isEmpty() && player instanceof ServerPlayer) {
				askPreview(MycWeapons.weaponId(b), MycWeapons.weaponId(d1), MycWeapons.weaponId(d2));
			}
			resultSlots.setItem(0, r);
		}

		@Override
		protected void onTake(Player player, ItemStack stack) {
			int b = MycWeapons.weaponId(inputSlots.getItem(0)), d1 = MycWeapons.weaponId(inputSlots.getItem(1)), d2 = MycWeapons.weaponId(inputSlots.getItem(2));
			if (player instanceof ServerPlayer sp) {
				taken(sp, b, d1, d2);
			}
			for (int i = 0; i < 3; i++) {
				inputSlots.setItem(i, ItemStack.EMPTY);
			}
		}

		@Override
		public void removed(Player player) {
			super.removed(player);
			if (player instanceof ServerPlayer) {
				clearContainer(player, inputSlots); // the weapons left in the slots go back to you
			}
		}

		@Override
		public boolean canTakeItemForPickAll(ItemStack stack, Slot slot) {
			return slot.container != resultSlots && super.canTakeItemForPickAll(stack, slot);
		}

		/** For MycWeapons.sync: what's in the input slots. */
		public ItemStack input(int i) {
			return inputSlots.getItem(i);
		}

		public void clearInput(int i) {
			inputSlots.setItem(i, ItemStack.EMPTY);
		}
	}

	/** The smithing table's screen (its picture and slots), with hints for the empty slots. */
	public static final class ForgeScreen extends ItemCombinerScreen<ForgeMenu> {
		private static final Identifier ERROR_SPRITE = Identifier.withDefaultNamespace("container/smithing/error");
		private static final Component BASE_HINT = Component.literal("Weapon base: the forged weapon is this one, with new attributes");
		private static final Component DONOR_HINT = Component.literal("Attribute donor: gives its first attribute");

		public ForgeScreen(ForgeMenu menu, Inventory inventory, Component title) {
			super(menu, inventory, title, Identifier.withDefaultNamespace("textures/gui/container/smithing.png"));
			titleLabelX = 44;
			titleLabelY = 15;
		}

		@Override
		public void extractRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partial) {
			super.extractRenderState(graphics, mouseX, mouseY, partial);
			Slot hovered = hoveredSlot;
			if (hovered != null && hovered.container != null && !hovered.hasItem() && hovered.index >= 0 && hovered.index < 3 && menu.getCarried().isEmpty()) {
				graphics.setTooltipForNextFrame(font, font.split(hovered.index == 0 ? BASE_HINT : DONOR_HINT, 115), mouseX, mouseY);
			} else if (blocked() && isHovering(65, 46, 28, 21, mouseX, mouseY)) {
				graphics.setTooltipForNextFrame(font, font.split(Component.literal(pendingSeq != 0 ? "Forging..." : "The Blacksmith doesn't take one of these"), 115), mouseX, mouseY);
			}
		}

		@Override
		protected void extractErrorIcon(GuiGraphicsExtractor graphics, int x, int y) {
			if (blocked()) {
				graphics.blitSprite(RenderPipelines.GUI_TEXTURED, ERROR_SPRITE, x + 65, y + 46, 28, 21);
			}
		}

		/** All three slots filled, but no result. */
		private boolean blocked() {
			return menu.input(0).getCount() > 0 && menu.input(1).getCount() > 0 && menu.input(2).getCount() > 0 && !menu.getSlot(menu.getResultSlot()).hasItem();
		}
	}
}
