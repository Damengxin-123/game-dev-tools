#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <vector>
#include <limits>

#ifdef _WIN32
#define API extern "C" __declspec(dllexport)
#else
#define API extern "C" __attribute__((visibility("default")))
#endif

// 双精度米制坐标，不依赖 Qt 或 Godot 引擎 ABI。
struct Point { double x; double y; };
// 连续存储的图形及包围盒，用于批量命中测试。
struct Shape {
    std::vector<Point> points; // 有序顶点。
    int kind; // 点集、折线、多边形。
    double width, diameter; // 米制线宽和点尺寸。
    double left, top, right, bottom; // 加速粗筛的边界。
};
// 跨调用保留的几何场景。
struct Scene { std::vector<Shape> shapes; };
// 返回三点有向面积的两倍。
static double cross(Point a, Point b, Point c) { return (b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x); }
// 求线段最近点，处理零长度边。
static Point closest(Point p, Point a, Point b) {
    const double dx=b.x-a.x,dy=b.y-a.y,len=dx*dx+dy*dy;
    const double t=len>1e-24?std::clamp(((p.x-a.x)*dx+(p.y-a.y)*dy)/len,0.,1.):0.;
    return {a.x+t*dx,a.y+t*dy};
}
// 计算平方距离，避免拾取时反复开方。
static double distance2(Point a,Point b) { return (a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y); }
// 使用奇偶规则判断面内部，兼容 Qt 默认填充规则。
static bool inside(const std::vector<Point>& p,Point v) {
    bool hit=false;
    for(size_t i=0,j=p.size()-1;i<p.size();j=i++)
        if((p[i].y>v.y)!=(p[j].y>v.y) && v.x<(p[j].x-p[i].x)*(v.y-p[i].y)/(p[j].y-p[i].y)+p[i].x) hit=!hit;
    return hit;
}
// 返回本地接口版本，启动时校验 DLL 是否匹配。
API int vg_version() { return 1; }
// 创建可复用的本地场景。
API Scene* vg_create() { return new Scene; }
// 释放场景拥有的所有缓存。
API void vg_destroy(Scene* s) { delete s; }
// 清空项目的几何索引。
API void vg_clear(Scene* s) { s->shapes.clear(); }
// 添加一个对象，同时缓存粗筛边界。
API void vg_add(Scene* s,const double* xy,int count,int kind,double width,double diameter) {
    if(count<1) return;
    Shape a; a.kind=kind;a.width=width;a.diameter=diameter;
    a.left=a.right=xy[0];a.top=a.bottom=xy[1];a.points.reserve(count);
    for(int i=0;i<count;++i) { Point p{xy[i*2],xy[i*2+1]};a.points.push_back(p);
        a.left=std::min(a.left,p.x);a.right=std::max(a.right,p.x);a.top=std::min(a.top,p.y);a.bottom=std::max(a.bottom,p.y); }
    s->shapes.push_back(std::move(a));
}
// 从顶层向底层命中，包围盒过滤后再进行精确几何检测。
API int vg_hit(Scene* s,double x,double y,double tolerance) {
    Point p{x,y};
    for(int i=static_cast<int>(s->shapes.size())-1;i>=0;--i) {
        const auto& a=s->shapes[i];const double r=std::max(tolerance,(a.kind==0?a.diameter:a.width)/2.);
        if(x<a.left-r||x>a.right+r||y<a.top-r||y>a.bottom+r)continue;
        if(a.kind==0) { for(auto q:a.points) if(distance2(p,q)<=r*r)return i; }
        else {
            if(a.kind==2&&inside(a.points,p))return i;
            const size_t edges=a.kind==2?a.points.size():a.points.size()-1;
            for(size_t j=0;j<edges;++j) if(distance2(p,closest(p,a.points[j],a.points[(j+1)%a.points.size()]))<=r*r)return i;
        }
    }
    return -1;
}
// 返回可插入顶点的最近边及投影坐标。
API int vg_nearest(const double* xy,int count,int closed,double x,double y,double tolerance,double* result) {
    int best=-1;double d=tolerance*tolerance;Point p{x,y};
    for(int i=0;i<count-(closed?0:1);++i) { int j=(i+1)%count;Point a{xy[2*i],xy[2*i+1]},b{xy[2*j],xy[2*j+1]};
        Point q=closest(p,a,b);double n=distance2(p,q);
        if(n<d&&distance2(q,a)>1e-16&&distance2(q,b)>1e-16) {d=n;best=i;result[0]=q.x;result[1]=q.y;}
    }return best;
}
// 沿完整折线累计虚线相位，避免在每个转角重新起笔。
API double* vg_dashes(const double* xy,int count,double dash,double gap,int* size) {
    std::vector<double> out; double phase=0;const double period=dash+gap;
    if(period<=0||dash<=0) {*size=0;return nullptr;}
    for(int i=1;i<count;++i) {
        Point a{xy[(i-1)*2],xy[(i-1)*2+1]},b{xy[i*2],xy[i*2+1]};double len=std::sqrt(distance2(a,b)),at=0;
        while(at<len-1e-9 && out.size()<4000000) {
            bool draw=phase<dash;double step=std::min(len-at,(draw?dash:period)-phase);
            if(step<1e-10) {phase=draw?dash:0;continue;}
            if(draw) {out.insert(out.end(),{a.x+(b.x-a.x)*at/len,a.y+(b.y-a.y)*at/len,a.x+(b.x-a.x)*(at+step)/len,a.y+(b.y-a.y)*(at+step)/len});}
            at+=step;phase+=step;if(phase>=period-1e-10)phase=0;
        }
    }
    *size=static_cast<int>(out.size());auto p=static_cast<double*>(std::malloc(out.size()*sizeof(double)));if(p)std::copy(out.begin(),out.end(),p);return p;
}
// 扫描带三角剖分：顶点和交叉点处分带，保持自交面的奇偶填充。
API double* vg_tessellate(const double* xy,int count,int* size) {
    *size=0;if(count<3)return nullptr;
    std::vector<Point> p;std::vector<double> ys;
    for(int i=0;i<count;++i) {p.push_back({xy[2*i],xy[2*i+1]});ys.push_back(xy[2*i+1]);}
    for(int i=0;i<count;++i) for(int j=i+2;j<count;++j) {
        if(i==0&&j==count-1)continue;
        Point a=p[i],b=p[(i+1)%count],c=p[j],d=p[(j+1)%count];
        double den=(b.x-a.x)*(d.y-c.y)-(b.y-a.y)*(d.x-c.x);if(std::abs(den)<1e-16)continue;
        double t=((c.x-a.x)*(d.y-c.y)-(c.y-a.y)*(d.x-c.x))/den;
        double u=((c.x-a.x)*(b.y-a.y)-(c.y-a.y)*(b.x-a.x))/den;
        if(t>0&&t<1&&u>0&&u<1)ys.push_back(a.y+t*(b.y-a.y));
    }
    std::sort(ys.begin(),ys.end());ys.erase(std::unique(ys.begin(),ys.end(),[](double a,double b){return std::abs(a-b)<1e-10;}),ys.end());
    std::vector<double> out;
    // 对指定高度计算边上的横坐标。
    auto at=[&](int e,double y) {const auto a=p[e],b=p[(e+1)%count];return a.x+(y-a.y)*(b.x-a.x)/(b.y-a.y);};
    for(size_t k=1;k<ys.size();++k) {
        double lo=ys[k-1],hi=ys[k],mid=(lo+hi)/2;std::vector<int> edges;
        for(int e=0;e<count;++e)if((p[e].y>mid)!=(p[(e+1)%count].y>mid))edges.push_back(e);
        std::sort(edges.begin(),edges.end(),[&](int a,int b){return at(a,mid)<at(b,mid);});
        for(size_t e=1;e<edges.size();e+=2) {
            Point a{at(edges[e-1],lo),lo},b{at(edges[e],lo),lo},c{at(edges[e],hi),hi},d{at(edges[e-1],hi),hi};
            if(std::abs(cross(a,b,c))>1e-14)out.insert(out.end(),{a.x,a.y,b.x,b.y,c.x,c.y});
            if(std::abs(cross(a,c,d))>1e-14)out.insert(out.end(),{a.x,a.y,c.x,c.y,d.x,d.y});
        }
    }
    *size=static_cast<int>(out.size());auto result=static_cast<double*>(std::malloc(out.size()*sizeof(double)));if(result)std::copy(out.begin(),out.end(),result);return result;
}
// 释放本地返回的连续几何数组。
API void vg_free(void* p) {std::free(p);}
