// Feature definitions for the template's jungle models that All That Simmers ships without one: its banyan figs
// and the two mangrove root clumps whose textures it has (the other four name textures it lacks).
static class JungleDefs
{
    const string Lua = """
-- Banyan figs and mangrove roots: models and textures from All That Simmers by Nikuksis (unused there), defined here.
local function tree(name, object, fx, blocking, height, energy, normal)
	return {
		name = name, description = "Jungle tree", object = object,
		footprintx = fx, footprintz = fx, blocking = blocking, upright = true,
		energy = energy, metal = 0, reclaimable = true, autoreclaimable = true, reclaimtime = energy * 6,
		damage = 400, flammable = true, crushresistance = blocking and 200 or 10, mass = 40,
		collisionvolumetype = "cylY", collisionvolumescales = (fx * 12) .. " " .. height .. " " .. (fx * 12), collisionvolumeoffsets = "0 " .. (height / 2) .. " 0",
		customparams = { author = "Nikuksis, 0 A.D.", category = "Vegetation", treeshader = "yes", randomrotate = "true", normaltex = normal },
	}
end
local defs = {
	tree("banyan_1", "banyan_1.s3o", 2, true, 184, 600, "unittextures/banyan_trunk_1_normal.dds"),
	tree("banyan_2", "banyan_2.s3o", 2, true, 115, 400, "unittextures/banyan_trunk_1_normal.dds"),
	tree("mangrove_1_1", "mangrove_1_1.s3o", 1, false, 42, 60, "unittextures/mangrove_roots_1_normal.dds"),
	tree("mangrove_2_1", "mangrove_2_1.s3o", 1, false, 43, 60, "unittextures/mangrove_roots_1_normal.dds"),
}
local out = {}
for _, d in ipairs(defs) do out[d.name] = d end
return out
""";

    public static void Write(string root) => File.WriteAllText(Path.Combine(root, "features", "starfall", "jungle.lua"), Lua.Replace("\r\n", "\n") + "\n");
}
