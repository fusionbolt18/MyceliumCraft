package dev.myceliumcraft;

/**
 * The name Mycelium gives a forged weapon, for the Blacksmith's result preview (MycForge). No
 * Minecraft classes, so the stand-in (LinkTestMain) can check it against Mycelium's.
 *
 * Mycelium's weapon name (WeaponStats.wpnName, as AttributeManager.UpdateAttName makes it) is each
 * attribute's name put in front of the plain name, in attribute order, so the last attribute comes
 * first: "Vigorous Avocado Flamberge". The forged weapon has only the donors' first attributes, the
 * first donor's then the second's, so it's "second first Plain": "Undergrown Feline Avocado Flamberge".
 */
public final class ForgeName {
	private ForgeName() {
	}

	/** baseName and baseAttributes ("Name: description; Name"), the donors' attribute lists. */
	public static String forged(String baseName, String baseAttributes, String donor1Attributes, String donor2Attributes) {
		String plain = plain(baseName, baseAttributes);
		String a1 = firstName(donor1Attributes), a2 = firstName(donor2Attributes);
		StringBuilder sb = new StringBuilder();
		if (!a2.isEmpty()) {
			sb.append(a2).append(' ');
		}
		if (!a1.isEmpty()) {
			sb.append(a1).append(' ');
		}
		return sb.append(plain).toString();
	}

	/** The weapon's name with its attributes' names taken off the front. */
	public static String plain(String name, String attributes) {
		String plain = name == null ? "" : name.trim();
		String[] names = names(attributes);
		boolean took = true;
		while (took) {
			took = false;
			for (String n : names) {
				if (!n.isEmpty() && plain.startsWith(n + " ")) {
					plain = plain.substring(n.length() + 1).trim();
					took = true;
				}
			}
		}
		return plain;
	}

	/** The first attribute's name ("Feline" from "Feline: meow; Undergrown: ..."). */
	public static String firstName(String attributes) {
		String[] names = names(attributes);
		return names.length > 0 ? names[0] : "";
	}

	private static String[] names(String attributes) {
		if (attributes == null || attributes.isBlank()) {
			return new String[0];
		}
		String[] parts = attributes.split("; ");
		for (int i = 0; i < parts.length; i++) {
			int colon = parts[i].indexOf(':');
			parts[i] = (colon >= 0 ? parts[i].substring(0, colon) : parts[i]).trim();
		}
		return parts;
	}
}
