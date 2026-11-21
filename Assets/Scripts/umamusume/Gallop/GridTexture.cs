using UnityEngine;

namespace Gallop
{
    // Port of Gallop.GridTexture (code_JP Gallop.GridTexture.c).
    // Selects one cell of a texture atlas laid out in rows of _columnNum cells by writing the
    // offset of _MainTex_ST (ShaderManager.PropertyId 0x2b).
    public class GridTexture
    {
        protected Material _material;   // 0x10
        protected float _sizeX;         // 0x18
        protected float _sizeY;         // 0x1C
        protected int _columnNum;       // 0x20
        protected bool _isValid;        // 0x24

        // GridTexture..ctor @0x7ffd59904d50
        public GridTexture(Material mat, float sizeX, float sizeY, int num)
        {
            _material = mat;
            _sizeX = sizeX;
            _sizeY = sizeY;
            _columnNum = num;
            if (num < 1 || sizeX <= 0f || sizeY <= 0f)
                _isValid = false;
            else
                _isValid = mat != null;
        }

        // GridTexture.SetId @0x7ffd59904c30
        // _MainTex_ST.zw = ((id % num) * sizeX, -sizeY * (id / num)); xy untouched.
        public virtual void SetId(int id)
        {
            if (!_isValid || id < 0) return;
            int num = _columnNum;
            int propertyId = ShaderManager.GetPropertyId(ShaderManager.PropertyId._MainTex_ST);
            Vector4 st = _material.GetVector(propertyId);
            st.z = (id % num) * _sizeX;
            st.w = -_sizeY * (id / num);
            _material.SetVector(propertyId, st);
        }
    }

    // Port of Gallop.GridTextureOffset (code_JP Gallop.GridTextureOffset.c).
    // Same cell math as GridTexture, written to _UVOffset (ShaderManager.PropertyId 0x156):
    // xy for the right eye (_isLeft false), zw for the left eye (_isLeft true).
    public class GridTextureOffset : GridTexture
    {
        protected bool _isLeft;         // 0x28

        // GridTextureOffset..ctor @0x7ffd59904c00
        public GridTextureOffset(Material mat, float sizeX, float sizeY, int num, bool left)
            : base(mat, sizeX, sizeY, num)
        {
            _isLeft = left;
        }

        // GridTextureOffset.SetId @0x7ffd59904ac0
        public override void SetId(int id)
        {
            if (!_isValid || id < 0) return;
            int num = _columnNum;
            int propertyId = ShaderManager.GetPropertyId(ShaderManager.PropertyId._UVOffset);
            Vector4 offset = _material.GetVector(propertyId);
            float u = (id % num) * _sizeX;
            float v = (id / num) * -_sizeY;
            if (!_isLeft)
            {
                offset.x = u;
                offset.y = v;
            }
            else
            {
                offset.z = u;
                offset.w = v;
            }
            _material.SetVector(propertyId, offset);
        }
    }
}
