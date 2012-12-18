﻿/*
 * Copyright 2010 www.wojilu.com
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *      http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using wojilu.DI;

namespace wojilu.Aop {

    /// <summary>
    /// Aop 容器，可以创建代理类，或者获取所有被监控的对象、方法
    /// </summary>
    public class AopContext {

        private static readonly ILog logger = LogManager.GetLogger( typeof( AopContext ) );

        private static Dictionary<Type, ObservedType> _oTypes = loadObservers();

        private static readonly Assembly _aopAssembly = loadCompiledAssembly();

        /// <summary>
        /// 获取所有被监控的类型
        /// </summary>
        /// <returns></returns>
        public static Dictionary<Type, ObservedType> GetObservedTypes() {
            return _oTypes;
        }

        /// <summary>
        /// 获取某方法的监控器
        /// </summary>
        /// <param name="t"></param>
        /// <param name="methodName"></param>
        /// <returns></returns>
        public static List<MethodObserver> GetMethodObservers( Type t, String methodName ) {

            List<ObservedMethod> list = _oTypes[t].MethodList;
            foreach (ObservedMethod x in list) {
                if (x.Method.Name == methodName) return x.Observer;
            }

            return null;
        }

        /// <summary>
        /// 获取某方法的 "混合运行" 监控器。为了避免被监控方法的多次调用，此监控器只返回第一个。
        /// </summary>
        /// <param name="t"></param>
        /// <param name="methodName"></param>
        /// <returns></returns>
        public static MethodObserver GetInvokeObserver( Type t, string methodName ) {

            List<MethodObserver> list = GetMethodObservers( t, methodName );
            foreach (MethodObserver x in list) {

                MethodInfo m = x.GetType().GetMethod( "Invoke" );

                if (m.DeclaringType != typeof( MethodObserver )) {

                    return x;

                }

            }

            return null;
        }

        /// <summary>
        /// 获取所有代理类所在的程序集
        /// </summary>
        /// <returns></returns>
        public static Assembly GetAopAssembly() {
            return _aopAssembly;
        }

        /// <summary>
        /// 根据类型创建对象。如果被监控，则创建代理类。否则返回自身的实例
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T CreateObject<T>() {
            T result = (T)CreateProxy( typeof( T ) );
            if (result == null) {
                return ObjectContext.Create<T>();
            }
            else {
                return result;
            }
        }

        /// <summary>
        /// 根据类型创建它的代理类。如果代理不存在，返回 null
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T CreateProxy<T>() {
            return (T)CreateProxy( typeof( T ) );
        }

        /// <summary>
        /// 根据类型创建它的代理类。如果代理不存在，返回 null
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public static Object CreateProxy( Type t ) {

            String name = strUtil.Join( t.Namespace, AopCoder.proxyClassPrefix + t.Name, "." );

            return _aopAssembly.CreateInstance( name );
        }

        private static Assembly loadCompiledAssembly() {

            Dictionary<Type, ObservedType> observers = loadObservers();

            String proxyCode = AopCoder.GetProxyClassCode( observers );

            logger.Info( proxyCode );

            return AopCoder.CompileCode( proxyCode, ObjectContext.Instance.AssemblyList );
        }

        private static Dictionary<Type, ObservedType> loadObservers() {

            Dictionary<Type, ObservedType> results = new Dictionary<Type, ObservedType>();

            Dictionary<String, Type> typeList = ObjectContext.Instance.TypeList;

            foreach (KeyValuePair<String, Type> kv in typeList) {

                Type type = kv.Value;

                if (type.IsSubclassOf( typeof( MethodObserver ) )) {

                    MethodObserver obj = rft.GetInstance( type ) as MethodObserver;
                    addType( results, obj );
                }

            }

            return results;
        }

        private static void addType( Dictionary<Type, ObservedType> results, MethodObserver obj ) {

            Dictionary<Type, String> dic = obj.GetRelatedMethods();
            foreach (KeyValuePair<Type, String> kv in dic) {

                List<MethodInfo> methods = getMethods( kv.Value, kv.Key );

                foreach (MethodInfo method in methods) {

                    addTypeSingle( results, obj, kv.Key, method );
                }

            }
        }

        private static void addTypeSingle( Dictionary<Type, ObservedType> results, MethodObserver obj, Type t, MethodInfo method ) {

            ObservedType oType;
            results.TryGetValue( t, out oType );
            if (oType == null) oType = new ObservedType();

            oType.Type = t;
            populateMethodList( oType, obj, method );

            results[t] = oType;
        }

        private static void populateMethodList( ObservedType oType, MethodObserver obj, MethodInfo method ) {

            if (oType.MethodList == null) {
                oType.MethodList = new List<ObservedMethod>();
            }

            if (hasAddMethod( oType, method )) {
                oType.MethodList = addObserverToMethodList( oType.MethodList, method, obj );
            }
            else {
                ObservedMethod om = addNewObserverMethod( obj, method );
                om.ObservedType = oType;
                oType.MethodList.Add( om );
            }
        }

        private static List<ObservedMethod> addObserverToMethodList( List<ObservedMethod> list, MethodInfo method, MethodObserver obj ) {

            foreach (ObservedMethod m in list) {
                if (m.Method == method) {
                    addObserverToMethodSingle( m, obj );
                }
            }

            return list;
        }

        private static void addObserverToMethodSingle( ObservedMethod m, MethodObserver obj ) {

            if (m.Observer == null) m.Observer = new List<MethodObserver>();

            if (m.Observer.Contains( obj )) return;

            m.Observer.Add( obj );
        }

        private static ObservedMethod addNewObserverMethod( MethodObserver objObserver, MethodInfo method ) {
            ObservedMethod om = new ObservedMethod();
            om.Method = method;
            om.Observer = new List<MethodObserver>();
            om.Observer.Add( objObserver );
            return om;
        }

        private static bool hasAddMethod( ObservedType ot, MethodInfo method ) {
            foreach (ObservedMethod x in ot.MethodList) {
                if (x.Method == method) {
                    return true;
                }
            }
            return false;
        }

        private static List<MethodInfo> getMethods( String strMethods, Type t ) {

            MethodInfo[] existMethods = t.GetMethods( BindingFlags.Public | BindingFlags.Instance );

            List<MethodInfo> list = new List<MethodInfo>();

            String[] arr = strMethods.Split( '/' );
            foreach (String item in arr) {

                if (strUtil.IsNullOrEmpty( item )) continue;

                MethodInfo x = getMethodInfo( existMethods, item, t );
                if (x == null) {
                    logger.Error( "method not exist. type=" + t.FullName + ", method=" + item );
                    continue;
                }

                if (list.Contains( x )) continue;

                list.Add( x );
            }

            return list;
        }

        private static MethodInfo getMethodInfo( MethodInfo[] existMethods, string item, Type t ) {

            foreach (MethodInfo x in existMethods) {
                if (x.Name == item) {

                    if (x.IsVirtual == false) {
                        logger.Error( "method is not virtual. type=" + t.FullName + ", method=" + item );
                        return null;
                    }
                    else {
                        return x;
                    }

                }

            }
            return null;
        }



    }

}
