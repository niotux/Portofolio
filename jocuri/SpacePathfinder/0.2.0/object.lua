function new_object(self, vtx, fac, scale, translate, rotate)
    self.vtx = vtx
    self.fac = fac
    self.scale = scale
    self.translate = translate
    self.rotate = rotate
    self.vtx_trans = {}

    function self.update()
        local cz = math.cos(self.rotate.z)
        local sz = math.sin(self.rotate.z)

        for i = 1, #self.vtx do
            self.vtx_trans[i] = {}
            self.vtx_trans[i].x = self.vtx[i].x * self.scale + self.translate.x
            self.vtx_trans[i].z = self.vtx[i].z * self.scale + self.translate.z

            x = self.vtx_trans[i].x
            z = self.vtx_trans[i].z

            self.vtx_trans[i].x = cz * x + sz * z
            self.vtx_trans[i].z = -sz * x + cz * z
        end

        for i = 1, #self.fac do
            if self.vtx_trans[self.fac[i][1]].z > 1 and self.vtx_trans[self.fac[i][2]].z > 1 then
                std3d.triangle({
                    {
                        x = self.vtx_trans[self.fac[i][1]].x * height / 2 / self.vtx_trans[self.fac[i][1]].z + width / 2,
                        y = 0.5 * height / 2 / self.vtx_trans[self.fac[i][1]].z + height / 2,
                        z = self.vtx_trans[self.fac[i][1]].z,
                        color = { 1, 0, 0 }
                    },
                    {
                        x = self.vtx_trans[self.fac[i][2]].x * height / 2 / self.vtx_trans[self.fac[i][2]].z + width / 2,
                        y = 0.5 * height / 2 / self.vtx_trans[self.fac[i][2]].z + height / 2,
                        z = self.vtx_trans[self.fac[i][2]].z,
                        color = { 0, 1, 0 }
                    },
                    {
                        x = self.vtx_trans[self.fac[i][2]].x * height / 2 / self.vtx_trans[self.fac[i][2]].z + width / 2,
                        y = -0.5 * height / 2 / self.vtx_trans[self.fac[i][2]].z + height / 2,
                        z = self.vtx_trans[self.fac[i][2]].z,
                        color = { 0, 0, 1 }
                    },
                    --self.vtx_trans[self.fac[i][1]].x * height / 2 / self.vtx_trans[self.fac[i][1]].z + width / 2,
                    ---0.5 * height / 2 / self.vtx_trans[self.fac[i][1]].z + height / 2
                })
            end
        end
    end
end
